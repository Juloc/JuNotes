import Foundation

/// Finds her subject for the name a timetable uses: "Mathematik", "Mathe" and "M" are one subject.
public enum SubjectMatcher {
    /// Names schools use for the same subject; each line is one subject.
    private static let aliases: [[String]] = [
        ["mathematik", "mathe", "ma", "m"],
        ["deutsch", "de", "d"],
        ["englisch", "english", "engl", "en", "e"],
        ["biologie", "bio", "bi"],
        ["chemie", "chem", "ch"],
        ["physik", "phy", "ph"],
        ["geschichte", "gesch", "ge", "gk"],
        ["geografie", "geographie", "erdkunde", "geo", "ek"],
        ["informatik", "info", "inf", "itg"],
        ["religion", "reli", "rel", "ev", "kath"],
        ["ethik", "eth"],
        ["sport", "spo", "sp"],
        ["musik", "mu"],
        ["kunst", "bk", "ku"],
        ["franzoesisch", "franzosisch", "franz", "fra", "f"],
        ["spanisch", "span", "spa"],
        ["latein", "lat", "l"],
        ["politik", "wirtschaft", "wipo", "gemeinschaftskunde", "gk", "sozialkunde"],
    ]

    /// Lower case, no accents, no dots or brackets, umlauts spelled out the German way.
    static func normalize(_ text: String) -> String {
        var result = text.lowercased()
        for (from, to) in [("ä", "ae"), ("ö", "oe"), ("ü", "ue"), ("ß", "ss")] { result = result.replacingOccurrences(of: from, with: to) }
        result = result.folding(options: .diacriticInsensitive, locale: nil)
        return result.filter { $0.isLetter || $0.isNumber || $0 == " " }.trimmingCharacters(in: .whitespaces)
    }

    private static func alias(of word: String) -> Int? {
        aliases.firstIndex { $0.contains(word) }
    }

    /// The best subject for a timetable entry such as "Mathematik", "Mathe Kl.9a" or "Klassenarbeit Mathe", or nil when none fits.
    /// Every word is tried in order; one-letter abbreviations only count as the first word, so "Test D" does not mean Deutsch by accident.
    public static func match(_ title: String, in subjects: [Subject]) -> Subject? {
        let words = normalize(title).split(separator: " ").map(String.init)
        let names = subjects.map { ($0, normalize($0.name)) }
        if let whole = names.first(where: { $0.1 == words.joined(separator: " ") }) { return whole.0 }
        for (position, word) in words.enumerated() where word.count > 1 || position == 0 {
            // 1. Same name, 2. same family of names, 3. one begins with the other (at least three letters).
            if let exact = names.first(where: { $0.1 == word }) { return exact.0 }
            if let family = alias(of: word), let found = names.first(where: { alias(of: $0.1.split(separator: " ").first.map(String.init) ?? $0.1) == family }) { return found.0 }
            if word.count >= 3, let prefix = names.first(where: { $0.1.count >= 3 && ($0.1.hasPrefix(word) || word.hasPrefix($0.1)) }) { return prefix.0 }
        }
        return nil
    }
}
