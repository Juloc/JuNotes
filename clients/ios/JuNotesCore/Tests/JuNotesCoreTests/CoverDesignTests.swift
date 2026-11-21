import Foundation
import Testing
@testable import JuNotesCore

struct CoverDesignTests {
    @Test func theServersAnswerBecomesACoverTheAppCanDraw() throws {
        let json = Data(##"{"colorA":"#6fb1ff","colorB":"#3478f6","emoji":null,"symbol":"function","pattern":"grid"}"##.utf8)
        let design = try JSONDecoder().decode(CoverDesign.self, from: json)
        let cover = design.cover
        #expect(cover.style == .gradient)
        #expect(cover.colorA == "#6fb1ff" && cover.colorB == "#3478f6")
        #expect(cover.symbol == "function" && cover.emoji == nil)
        #expect(cover.pattern == .grid)
    }

    @Test func anEmojiWinsOverASymbolAndAnUnknownPatternIsNone() throws {
        let json = Data(##"{"colorA":"#ff9f0a","colorB":"#ffd15c","emoji":"🧸","symbol":"star","pattern":"spiral"}"##.utf8)
        let cover = try JSONDecoder().decode(CoverDesign.self, from: json).cover
        #expect(cover.emoji == "🧸" && cover.symbol == nil)
        #expect(cover.pattern == .none)
    }

    @Test func theCoverTaskHasItsOwnPath() {
        #expect(AITask.cover.rawValue == "cover")
    }
}
