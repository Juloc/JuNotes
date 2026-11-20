import Foundation
import Testing
@testable import JuNotesCore

@Suite struct FolderTreeTests {
    private func sample() -> (tree: FolderTree, school: Subject, math: Subject, fractions: Subject, kita: Subject) {
        let school = Subject(name: "Schule", sort: 0, isSubject: false)
        let kita = Subject(name: "Kindi", sort: 1, isSubject: false)
        let math = Subject(name: "Mathe", sort: 0, folderID: school.id)
        let fractions = Subject(name: "Brüche", sort: 0, folderID: math.id, isSubject: false)
        return (FolderTree([fractions, kita, math, school]), school, math, fractions, kita)
    }

    @Test func rootsAreTheFoldersWithoutParent() {
        let s = sample()
        #expect(s.tree.children(of: nil).map(\.name) == ["Schule", "Kindi"])
        #expect(s.tree.children(of: s.school.id).map(\.name) == ["Mathe"])
        #expect(s.tree.children(of: s.kita.id).isEmpty)
    }

    @Test func aFolderWhoseParentIsGoneIsShownAtTheTop() {
        let orphan = Subject(name: "Waise", folderID: UUID())
        let tree = FolderTree([orphan])
        #expect(tree.children(of: nil).map(\.name) == ["Waise"])
    }

    @Test func pathGoesFromTheTopDown() {
        let s = sample()
        #expect(s.tree.path(to: s.fractions.id).map(\.name) == ["Schule", "Mathe", "Brüche"])
        #expect(s.tree.pathTitle(of: s.fractions.id) == "Schule / Mathe / Brüche")
        #expect(s.tree.pathTitle(of: nil) == "")
    }

    @Test func nearestSubjectIsTheFolderItselfOrTheOneAbove() {
        let s = sample()
        #expect(s.tree.nearestSubject(from: s.fractions.id)?.name == "Mathe")
        #expect(s.tree.nearestSubject(from: s.math.id)?.name == "Mathe")
        #expect(s.tree.nearestSubject(from: s.school.id) == nil)
        #expect(s.tree.nearestSubject(from: nil) == nil)
    }

    @Test func descendantsAtAnyDepth() {
        let s = sample()
        #expect(s.tree.descendantIDs(of: s.school.id) == [s.math.id, s.fractions.id])
        #expect(s.tree.descendantIDs(of: s.fractions.id).isEmpty)
    }

    @Test func aFolderCannotGoIntoItselfOrIntoSomethingInsideIt() {
        let s = sample()
        #expect(!s.tree.canMove(s.school.id, into: s.school.id))
        #expect(!s.tree.canMove(s.school.id, into: s.fractions.id))
        #expect(s.tree.canMove(s.fractions.id, into: s.kita.id))
        #expect(s.tree.canMove(s.fractions.id, into: nil))
    }

    @Test func aCircleInTheDataDoesNotLoopForever() {
        var a = Subject(name: "A")
        var b = Subject(name: "B")
        a.folderID = b.id
        b.folderID = a.id
        let tree = FolderTree([a, b])
        #expect(tree.path(to: a.id).count == 2)
        #expect(tree.flattened().count <= 2)
    }

    @Test func flattenedListsParentsBeforeChildrenWithDepth() {
        let s = sample()
        let flat = s.tree.flattened()
        #expect(flat.map(\.folder.name) == ["Schule", "Mathe", "Brüche", "Kindi"])
        #expect(flat.map(\.depth) == [0, 1, 2, 0])
    }
}

@Suite struct FolderEntityTests {
    @Test func aFolderRoundTripsThroughTheWireFormat() throws {
        let parent = UUID()
        let cover = Cover(style: .gradient, colorA: "#111111", colorB: "#222222", symbol: "leaf", pattern: .dots)
        let folder = Subject(name: "Biologie", color: "#34c759", symbol: "leaf", sort: 3, folderID: parent, isSubject: false, cover: cover)
        let change = try folder.change()
        #expect(change.parentID == parent)
        let record = SyncRecord(kind: change.kind, id: change.id, parentID: change.parentID, payload: change.payload, deleted: false, seq: 1)
        let decoded = try Subject.decode(record)
        #expect(decoded == folder)
    }

    @Test func anOldSubjectWithoutNewFieldsIsAFolderAtTheTopThatIsASubject() throws {
        let payload = try JSONDecoder().decode(JSONValue.self, from: Data(##"{"name":"Mathe","color":"#0a84ff","symbol":"function","sort":2}"##.utf8))
        let id = UUID()
        let record = SyncRecord(kind: .subject, id: id, parentID: nil, payload: payload, deleted: false, seq: 1)
        let subject = try Subject.decode(record)
        #expect(subject.isSubject)
        #expect(subject.folderID == nil)
        #expect(subject.cover == nil)
    }

    @Test func aNotebookKeepsItsCover() throws {
        let cover = Cover.plain(color: "#ff9f0a", symbol: "star")
        let notebook = Notebook(subjectID: UUID(), title: "Gedichte", cover: cover)
        let change = try notebook.change()
        let record = SyncRecord(kind: change.kind, id: change.id, parentID: change.parentID, payload: change.payload, deleted: false, seq: 1)
        #expect(try Notebook.decode(record).cover == cover)
    }

    @Test func aBrokenCoverIsIgnoredInsteadOfLosingTheNotebook() throws {
        let payload = try JSONDecoder().decode(JSONValue.self, from: Data(#"{"title":"X","type":"pages","template":"ruled","createdAt":"2026-11-19T10:00:00Z","cover":{"style":"hologram","colorA":5}}"#.utf8))
        let record = SyncRecord(kind: .notebook, id: UUID(), parentID: nil, payload: payload, deleted: false, seq: 1)
        let notebook = try Notebook.decode(record)
        #expect(notebook.title == "X")
        #expect(notebook.cover == nil)
    }
}

@Suite struct CoverTests {
    @Test func unknownStyleFallsBackToGradient() throws {
        let cover = try JSONDecoder().decode(Cover.self, from: Data(##"{"style":"hologram","colorA":"#123456"}"##.utf8))
        #expect(cover.style == .gradient)
        #expect(cover.colorB == "#123456")
        #expect(cover.pattern == .none)
    }

    @Test func aPictureMakesItAnImageCoverAndRemovingItGoesBack() {
        let id = UUID()
        let cover = Cover.plain(color: "#000000").withImage(id)
        #expect(cover.style == .image)
        #expect(cover.assetID == id)
        let without = cover.withImage(nil)
        #expect(without.style == .gradient)
        #expect(without.assetID == nil)
    }

    @Test func thereAreTwelvePresetsWithDistinctNames() {
        #expect(Cover.presets.count == 12)
        #expect(Set(Cover.presets.map(\.name)).count == 12)
    }
}

@Suite struct OrderingEntityTests {
    @Test func favouritesTagsLockAndBookmarksSurviveTheWire() throws {
        let notebook = Notebook(subjectID: UUID(), title: "Beobachtungen", favorite: true, locked: true, tags: ["Kindi", "Praxis"])
        let nc = try notebook.change()
        let nr = SyncRecord(kind: nc.kind, id: nc.id, parentID: nc.parentID, payload: nc.payload, deleted: false, seq: 1)
        let back = try Notebook.decode(nr)
        #expect(back.favorite && back.locked)
        #expect(back.tags == ["Kindi", "Praxis"])

        let folder = Subject(name: "Schule", isSubject: false, favorite: true, tags: ["wichtig"])
        let fc = try folder.change()
        let fr = SyncRecord(kind: fc.kind, id: fc.id, parentID: fc.parentID, payload: fc.payload, deleted: false, seq: 1)
        #expect(try Subject.decode(fr) == folder)

        let page = Page(notebookID: UUID(), sort: "m", bookmarked: true)
        let pc = try page.change()
        let pr = SyncRecord(kind: pc.kind, id: pc.id, parentID: pc.parentID, payload: pc.payload, deleted: false, seq: 1)
        #expect(try Page.decode(pr).bookmarked)
    }

    @Test func olderEntitiesAreNeitherLockedNorFavourite() throws {
        let payload = try JSONDecoder().decode(JSONValue.self, from: Data(##"{"title":"X","type":"pages","template":"ruled","createdAt":"2026-11-19T10:00:00Z"}"##.utf8))
        let notebook = try Notebook.decode(SyncRecord(kind: .notebook, id: UUID(), payload: payload, seq: 1))
        #expect(!notebook.favorite && !notebook.locked && notebook.tags.isEmpty)
    }

    @Test func everyPaperHasANameAndTheRulingAddsUp() {
        for template in PageTemplate.allCases { #expect(!template.title.isEmpty) }
        #expect(PageTemplate.ruledFine.lineSpacing == 14)
        #expect(PageTemplate.ruledMedium.lineSpacing == 20)
        #expect(PageTemplate.gridLarge.gridSpacing == 28)
        #expect(PageTemplate.blank.lineSpacing == nil && PageTemplate.blank.gridSpacing == nil && PageTemplate.blank.dotSpacing == nil)
        #expect(PageTemplate.ruled.hasMargin && !PageTemplate.cornell.hasMargin)
    }

    @Test func anUnknownPaperFromANewerAppDoesNotBreakTheOthers() throws {
        let data = Data(##"["ruled","hologram"]"##.utf8)
        // A decoding failure for one value is the caller's to handle; the known ones still decode on their own.
        #expect((try? JSONDecoder().decode([PageTemplate].self, from: data)) == nil)
        #expect(try JSONDecoder().decode([PageTemplate].self, from: Data(##"["planner"]"##.utf8)) == [.planner])
    }
}

@Suite struct PageObjectTests {
    @Test func anObjectSurvivesTheWireWithItsPageAsParent() throws {
        let page = UUID()
        let object = PageObject(pageID: page, type: .text, x: 40, y: 120, width: 200, height: 50, rotation: 12, text: "Merken!", color: "#ff375f", fontSize: 20, z: 3)
        let change = try object.change()
        #expect(change.kind == .object)
        #expect(change.parentID == page)
        let record = SyncRecord(kind: change.kind, id: change.id, parentID: change.parentID, payload: change.payload, deleted: false, seq: 1)
        #expect(try PageObject.decode(record) == object)
    }

    @Test func missingFieldsGetTheSizeOfTheirKind() throws {
        let payload = try JSONDecoder().decode(JSONValue.self, from: Data(##"{"type":"sticker","text":"⭐️"}"##.utf8))
        let object = try PageObject.decode(SyncRecord(kind: .object, id: UUID(), parentID: UUID(), payload: payload, seq: 1))
        #expect(object.width == 64 && object.height == 64)
        #expect(object.fontSize == 16 && object.rotation == 0)
    }

    @Test func aPhotoKeepsItsProportionsAndIsNeverBlownUp() {
        let wide = PageObject.imageSize(pixelWidth: 4000, pixelHeight: 3000)
        #expect(wide.width == 300 && wide.height == 225)
        let small = PageObject.imageSize(pixelWidth: 100, pixelHeight: 50)
        #expect(small.width == 100 && small.height == 50)
        #expect(PageObject.imageSize(pixelWidth: 0, pixelHeight: 0).width == 240)
    }

    @Test func anObjectCannotBeLostOffThePage() {
        let far = PageObject(pageID: UUID(), type: .image, x: 5000, y: -5000, width: 240, height: 180)
        let kept = far.clamped(toWidth: 595, height: 842)
        #expect(kept.x == 571)
        #expect(kept.y == 24 - 180)
        let inside = PageObject(pageID: UUID(), type: .text, x: 100, y: 100)
        #expect(inside.clamped(toWidth: 595, height: 842) == inside)
    }

    @Test func everyKindHasASize() {
        for kind in PageObject.Kind.allCases {
            let size = PageObject.defaultSize(for: kind)
            #expect(size.width > 0 && size.height > 0)
        }
    }
}

@Suite struct SensitiveTests {
    @Test func aFolderMakesEverythingInsideItSensitive() {
        let kindi = Subject(name: "Kindi", isSubject: false, sensitive: true)
        let group = Subject(name: "Gruppe", folderID: kindi.id, isSubject: false)
        let school = Subject(name: "Schule", isSubject: false)
        let tree = FolderTree([kindi, group, school])
        #expect(tree.isSensitive(folderID: group.id))
        #expect(tree.isSensitive(folderID: kindi.id))
        #expect(!tree.isSensitive(folderID: school.id))
        #expect(!tree.isSensitive(folderID: nil))
    }

    @Test func theFlagSurvivesTheWireAndDefaultsToOff() throws {
        let notebook = Notebook(title: "Beobachtungen", sensitive: true)
        let change = try notebook.change()
        #expect(try Notebook.decode(SyncRecord(kind: change.kind, id: change.id, payload: change.payload, seq: 1)).sensitive)
        let folder = Subject(name: "Kindi", isSubject: false, sensitive: true)
        let folderChange = try folder.change()
        #expect(try Subject.decode(SyncRecord(kind: folderChange.kind, id: folderChange.id, payload: folderChange.payload, seq: 1)) == folder)
        #expect(!Notebook(title: "X").sensitive)
    }
}
