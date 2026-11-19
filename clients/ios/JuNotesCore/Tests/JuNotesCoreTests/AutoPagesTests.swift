import Testing
@testable import JuNotesCore

struct AutoPagesTests {
    @Test func writingNearTheBottomOfTheLastPageAddsAPage() {
        #expect(AutoPages.needsNewPage(writingAtY: 900, pageHeight: 1000, isLastPage: true))
        #expect(AutoPages.needsNewPage(writingAtY: 850, pageHeight: 1000, isLastPage: true))
    }

    @Test func writingHigherUpOrOnAnEarlierPageAddsNothing() {
        #expect(!AutoPages.needsNewPage(writingAtY: 849, pageHeight: 1000, isLastPage: true))
        #expect(!AutoPages.needsNewPage(writingAtY: 990, pageHeight: 1000, isLastPage: false))
        #expect(!AutoPages.needsNewPage(writingAtY: 10, pageHeight: 0, isLastPage: true))
    }

    @Test func emptyAutoPagesAtTheEndAreDropped() {
        let first = PageState(isEmpty: false, wasAutoCreated: false)
        let second = PageState(isEmpty: true, wasAutoCreated: true)
        let third = PageState(isEmpty: true, wasAutoCreated: true)
        #expect(AutoPages.trimmed([first, second, third]) == [first])
    }

    @Test func aPageSheAddedHerselfStaysEvenWhenEmpty() {
        let first = PageState(isEmpty: false, wasAutoCreated: false)
        let mine = PageState(isEmpty: true, wasAutoCreated: false)
        let auto = PageState(isEmpty: true, wasAutoCreated: true)
        #expect(AutoPages.trimmed([first, mine, auto]) == [first, mine])
    }

    @Test func writtenAutoPagesStay() {
        let first = PageState(isEmpty: false, wasAutoCreated: false)
        let written = PageState(isEmpty: false, wasAutoCreated: true)
        #expect(AutoPages.trimmed([first, written]) == [first, written])
    }

    @Test func oneEmptyPageAlwaysRemains() {
        let only = PageState(isEmpty: true, wasAutoCreated: true)
        #expect(AutoPages.trimmed([only]) == [only])
    }

    @Test func positionCountsFromOne() {
        #expect(AutoPages.position(of: 2, in: 7)?.current == 3)
        #expect(AutoPages.position(of: 2, in: 7)?.total == 7)
        #expect(AutoPages.position(of: 7, in: 7) == nil)
        #expect(AutoPages.position(of: 0, in: 0) == nil)
    }
}
