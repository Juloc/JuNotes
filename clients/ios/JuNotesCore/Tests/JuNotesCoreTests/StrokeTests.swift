import Foundation
import Testing
@testable import JuNotesCore

struct StrokeTests {
    let page = UUID(uuidString: "00000000-0000-0000-0000-000000000001")!

    func stroke(_ n: Int) -> Stroke {
        Stroke(id: UUID(uuidString: "00000000-0000-0000-0000-00000000010\(n)")!, points: [StrokePoint(x: Double(n), y: 2)])
    }

    @Test func aDocumentSurvivesEncodingAndDecoding() throws {
        let document = StrokeDocument(pageID: page, strokes: [
            Stroke(tool: .marker, color: "#ffcc00", width: 12, points: [StrokePoint(x: 1.5, y: 2.5, pressure: 0.4, altitude: 0.9, azimuth: 1.1, roll: 0.3, time: 0.02)]),
        ])
        #expect(try StrokeDocument.decode(document.encoded()) == document)
    }

    @Test func theEncodingIsStable() throws {
        let document = StrokeDocument(pageID: page, strokes: [stroke(1)])
        #expect(try document.encoded() == document.encoded())
    }

    @Test func aNewerFormatIsRefusedInsteadOfBeingMisread() throws {
        var document = StrokeDocument(pageID: page)
        document.version = StrokeDocument.currentVersion + 1
        let data = try document.encoded()
        #expect(throws: StrokeDocument.DecodingFailure.newerVersion(2)) { try StrokeDocument.decode(data) }
    }

    @Test func mergingKeepsStrokesFromBothSidesOnce() {
        let left = StrokeDocument(pageID: page, strokes: [stroke(1), stroke(2)])
        let right = StrokeDocument(pageID: page, strokes: [stroke(2), stroke(3)])
        #expect(left.merged(with: right).strokes.map(\.id) == [stroke(1), stroke(2), stroke(3)].map(\.id))
    }

    @Test func mergingIsTheSameInEitherOrderAsASet() {
        let left = StrokeDocument(pageID: page, strokes: [stroke(1)])
        let right = StrokeDocument(pageID: page, strokes: [stroke(2)])
        #expect(Set(left.merged(with: right).strokes.map(\.id)) == Set(right.merged(with: left).strokes.map(\.id)))
    }

    @Test func theSVGPathFollowsThePoints() {
        let stroke = Stroke(points: [StrokePoint(x: 0, y: 0), StrokePoint(x: 10.126, y: 5.5), StrokePoint(x: 20, y: 0)])
        #expect(stroke.svgPath == "M0 0 L10.13 5.5 L20 0")
        #expect(Stroke(points: []).svgPath == "")
    }
}
