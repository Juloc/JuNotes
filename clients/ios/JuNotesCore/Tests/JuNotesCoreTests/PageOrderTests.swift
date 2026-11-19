import Testing
@testable import JuNotesCore

struct PageOrderTests {
    @Test func theFirstPageGetsAMiddleKey() { #expect(PageOrder.key(after: nil, before: nil) == "i") }

    @Test func appendedKeysAscend() {
        let keys = PageOrder.keys(appending: 100, after: nil)
        #expect(keys == keys.sorted())
        #expect(Set(keys).count == 100)
    }

    @Test func aKeyBetweenTwoSortsBetweenThem() {
        let middle = PageOrder.key(after: "a", before: "c")
        #expect("a" < middle && middle < "c")
    }

    @Test func aKeyBeforeTheFirstSortsBeforeIt() {
        let before = PageOrder.key(after: nil, before: "i")
        #expect(before < "i")
    }

    @Test func keysBetweenNeighboursNeverRunOut() {
        var lower = "a"
        let upper = "b"
        for _ in 0..<300 {
            let next = PageOrder.key(after: lower, before: upper)
            #expect(lower < next && next < upper)
            lower = next
        }
    }

    @Test func insertingAtTheFrontRepeatedlyKeepsTheOrder() {
        var first = "i"
        for _ in 0..<100 {
            let next = PageOrder.key(after: nil, before: first)
            #expect(next < first)
            first = next
        }
    }

    @Test func insertingAlwaysInTheSameGapKeepsTheOrder() {
        let low = "a", high = "b"
        var keys = [low, high]
        for _ in 0..<60 {
            keys.insert(PageOrder.key(after: keys[0], before: keys[1]), at: 1)
        }
        #expect(keys == keys.sorted())
        #expect(Set(keys).count == keys.count)
    }

    @Test func twoDevicesInsertingIntoTheSameGapStayOrdered() {
        // Different devices may pick equal keys; the page id is the tie-break, so the keys need only be valid, never unique.
        let a = PageOrder.key(after: "a", before: "c")
        let b = PageOrder.key(after: "a", before: "c")
        #expect(a == b)
        #expect("a" < a && a < "c")
    }
}
