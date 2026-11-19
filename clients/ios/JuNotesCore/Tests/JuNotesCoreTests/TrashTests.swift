import Foundation
import Testing
@testable import JuNotesCore

struct TrashTests {
    let now = Date(timeIntervalSince1970: 1_800_000_000)

    @Test func itStaysForThirtyDays() {
        let trashed = now - 29 * 86400
        #expect(!Trash.isPurgeable(trashedAt: trashed, now: now))
        #expect(Trash.isPurgeable(trashedAt: now - 30 * 86400, now: now))
        #expect(Trash.isPurgeable(trashedAt: now - 90 * 86400, now: now))
    }

    @Test func nothingOutsideTheTrashIsPurgeable() { #expect(!Trash.isPurgeable(trashedAt: nil, now: now)) }

    @Test func daysLeftCountUpAndStopAtZero() {
        #expect(Trash.daysLeft(trashedAt: now, now: now) == 30)
        #expect(Trash.daysLeft(trashedAt: now - 29 * 86400 - 3600, now: now) == 1)
        #expect(Trash.daysLeft(trashedAt: now - 40 * 86400, now: now) == 0)
    }

    @Test func movingToTheTrashAndBackLeavesTheRestAlone() {
        let notebook = Notebook(id: uuid(2), title: "Mathe", createdAt: now)
        let trashed = notebook.movedToTrash(at: now)

        #expect(trashed.trashedAt == now)
        #expect(trashed.restored() == notebook)
        let page = Page(id: uuid(3), notebookID: uuid(2), sort: "i")
        #expect(page.movedToTrash(at: now).restored() == page)
    }

    @Test func thePurgeDateIsThirtyDaysOut() { #expect(Trash.purgeDate(trashedAt: now) == now + 30 * 86400) }
}
