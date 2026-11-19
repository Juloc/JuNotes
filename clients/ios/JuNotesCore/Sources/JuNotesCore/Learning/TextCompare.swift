import Foundation

/// How well she wrote a text from memory: the original word by word, with what she left out marked.
public struct TextComparison: Sendable, Equatable {
    public struct Word: Sendable, Equatable {
        public var text: String
        public var missing: Bool
    }

    /// The original, each word marked present or missing.
    public var words: [Word]
    /// Words she wrote that are not in the original, which may be errors of her own or of the handwriting recognition.
    public var extra: [String]

    public var missing: [String] { words.filter(\.missing).map(\.text) }

    /// Share of the original she remembered, from 0 to 1.
    public var score: Double {
        words.isEmpty ? 1 : Double(words.filter { !$0.missing }.count) / Double(words.count)
    }
}

public enum TextCompare {
    /// Compares by words in order, tolerant of the small mistakes handwriting recognition makes: a missed accent or one wrong letter in a longer word is still the word.
    /// Longer texts are compared up to this many words each; a page has a few hundred, and the table grows with the product of both lengths.
    public static let maximumWords = 1500

    public static func compare(original: String, recalled: String, limit: Int = TextCompare.maximumWords) -> TextComparison {
        let source = Array(words(of: original).prefix(limit))
        let written = Array(words(of: recalled).prefix(limit))

        // Each distinct word gets a number, and each pair of distinct words is judged once: the table below asks millions of times.
        var ids: [String: Int] = [:]
        func id(_ key: String) -> Int {
            if let known = ids[key] { return known }
            ids[key] = ids.count
            return ids.count - 1
        }
        let sourceIDs = source.map { id($0.key) }
        let writtenIDs = written.map { id($0.key) }
        let keys = ids.sorted { $0.value < $1.value }.map(\.key)
        let lengths = keys.map(\.count)
        // A bit per letter present: one edit changes at most two bits, so words whose masks differ by more cannot be close, and that settles nearly every pair.
        let masks = keys.map { key in key.unicodeScalars.reduce(UInt64(0)) { $0 | (1 << UInt64($1.value % 64)) } }
        var verdicts: [Int: Bool] = [:]
        func matches(_ a: Int, _ b: Int) -> Bool {
            if a == b { return true }
            let longest = max(lengths[a], lengths[b])
            let tolerance = longest >= 9 ? 2 : longest >= 5 ? 1 : 0
            if tolerance == 0 || abs(lengths[a] - lengths[b]) > tolerance || (masks[a] ^ masks[b]).nonzeroBitCount > 2 * tolerance { return false }
            let pair = min(a, b) * keys.count + max(a, b)
            if let known = verdicts[pair] { return known }
            let result = distance(keys[a], keys[b], limit: tolerance) <= tolerance
            verdicts[pair] = result
            return result
        }

        // Longest common subsequence under the tolerant equality.
        var table = Array(repeating: Array(repeating: 0, count: written.count + 1), count: source.count + 1)
        for i in stride(from: source.count - 1, through: 0, by: -1) {
            for j in stride(from: written.count - 1, through: 0, by: -1) {
                table[i][j] = matches(sourceIDs[i], writtenIDs[j]) ? table[i + 1][j + 1] + 1 : max(table[i + 1][j], table[i][j + 1])
            }
        }

        var matchedSource = Set<Int>(), matchedWritten = Set<Int>()
        var i = 0, j = 0
        while i < source.count, j < written.count {
            if matches(sourceIDs[i], writtenIDs[j]) {
                matchedSource.insert(i)
                matchedWritten.insert(j)
                i += 1
                j += 1
            } else if table[i + 1][j] >= table[i][j + 1] {
                i += 1
            } else {
                j += 1
            }
        }

        return TextComparison(
            words: source.enumerated().map { .init(text: $0.element.shown, missing: !matchedSource.contains($0.offset)) },
            extra: written.enumerated().filter { !matchedWritten.contains($0.offset) }.map(\.element.shown)
        )
    }

    private static func words(of text: String) -> [(shown: String, key: String)] {
        text.split { $0.isWhitespace }.compactMap { piece in
            let shown = String(piece).trimmingCharacters(in: .punctuationCharacters.union(.symbols))
            let key = normalize(shown)
            return key.isEmpty ? nil : (shown, key)
        }
    }

    /// Lower case, umlauts spelled as written German does, no other accents.
    static func normalize(_ word: String) -> String {
        var result = word.lowercased()
        for (from, to) in [("ä", "ae"), ("ö", "oe"), ("ü", "ue"), ("ß", "ss")] { result = result.replacingOccurrences(of: from, with: to) }
        return result.folding(options: .diacriticInsensitive, locale: nil).filter { $0.isLetter || $0.isNumber }
    }

    static func same(_ a: String, _ b: String) -> Bool {
        if a == b { return true }
        let longest = max(a.count, b.count)
        // Short words must match exactly: "ist" and "ost" are different words. Longer ones may be a letter or two off.
        let tolerance = longest >= 9 ? 2 : longest >= 5 ? 1 : 0
        return tolerance > 0 && abs(a.count - b.count) <= tolerance && distance(a, b, limit: tolerance) <= tolerance
    }

    private static func distance(_ a: String, _ b: String, limit: Int) -> Int {
        let a = Array(a), b = Array(b)
        var previous = Array(0...b.count)
        for (i, left) in a.enumerated() {
            var current = [i + 1]
            for (j, right) in b.enumerated() { current.append(min(previous[j + 1] + 1, current[j] + 1, previous[j] + (left == right ? 0 : 1))) }
            if current.min()! > limit { return limit + 1 }
            previous = current
        }
        return previous[b.count]
    }
}
