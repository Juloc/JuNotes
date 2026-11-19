import Foundation
import Testing
@testable import JuNotesCore

struct SyncCodingTests {
    /// What the server sends: camelCase names, lower-case ids, nulls spelled out.
    let serverJSON = """
    {"changes":[
      {"kind":"stroke","id":"00000000-0000-0000-0000-000000000101","parentId":"00000000-0000-0000-0000-000000000001","payload":{"n":1,"tags":["a",true,null]},"deleted":false,"seq":5},
      {"kind":"stroke","id":"00000000-0000-0000-0000-000000000102","parentId":"00000000-0000-0000-0000-000000000001","payload":null,"deleted":true,"seq":6}
    ],"cursor":6,"more":false}
    """

    @Test func theServersPullResponseDecodes() throws {
        let response = try JSONDecoder().decode(PullResponse.self, from: Data(serverJSON.utf8))

        #expect(response.cursor == 6)
        #expect(!response.more)
        #expect(response.changes.count == 2)
        #expect(response.changes[0].parentID == uuid(1))
        #expect(response.changes[0].payload == .object(["n": .number(1), "tags": .array([.string("a"), .bool(true), .null])]))
        #expect(response.changes[1].deleted)
        #expect(response.changes[1].payload == nil)
    }

    @Test func aChangeIsSentWithTheNamesTheServerExpects() throws {
        let data = try JSONEncoder().encode(PushRequest(changes: [stroke(1)]))
        let object = try #require(JSONSerialization.jsonObject(with: data) as? [String: Any])
        let change = try #require((object["changes"] as? [[String: Any]])?.first)

        #expect(change["kind"] as? String == "stroke")
        #expect(change["parentId"] as? String == uuid(1).uuidString)
        #expect(change["deleted"] as? Bool == false)
        #expect(change["parentID"] == nil)
    }

    @Test func aPushResponseDecodes() throws {
        let response = try JSONDecoder().decode(PushResponse.self, from: Data(#"{"accepted":3,"seq":17}"#.utf8))
        #expect(response == PushResponse(accepted: 3, seq: 17))
    }

    @Test func jsonValuesSurviveARoundTrip() throws {
        let value = JSONValue.object(["a": .array([.number(1.5), .string("x"), .null, .bool(false)]), "b": .object([:])])
        #expect(try JSONDecoder().decode(JSONValue.self, from: JSONEncoder().encode(value)) == value)
    }

    @Test func aWholeNumberIsNotMistakenForABoolean() throws {
        #expect(try JSONDecoder().decode(JSONValue.self, from: Data("1".utf8)) == .number(1))
        #expect(try JSONDecoder().decode(JSONValue.self, from: Data("0".utf8)) == .number(0))
    }
}
