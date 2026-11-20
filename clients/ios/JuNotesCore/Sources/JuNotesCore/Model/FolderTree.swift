import Foundation

/// The folders as a tree. A folder whose parent is gone counts as a root, so nothing she made ever disappears from view.
public struct FolderTree: Sendable {
    public let folders: [Subject]
    private let byID: [UUID: Subject]

    public init(_ folders: [Subject]) {
        self.folders = folders
        var map: [UUID: Subject] = [:]
        for folder in folders { map[folder.id] = folder }
        byID = map
    }

    public func folder(_ id: UUID) -> Subject? { byID[id] }

    /// The folders directly inside `parent`; the top level when nil.
    public func children(of parent: UUID?) -> [Subject] {
        folders
            .filter { folder in
                if let parent { return folder.folderID == parent }
                return folder.folderID == nil || byID[folder.folderID!] == nil
            }
            .sorted { ($0.sort, $0.name.lowercased(), $0.id.uuidString) < ($1.sort, $1.name.lowercased(), $1.id.uuidString) }
    }

    /// From the top level down to the folder itself. Stops where the chain would go round in a circle.
    public func path(to id: UUID) -> [Subject] {
        var result: [Subject] = []
        var seen: Set<UUID> = []
        var current: UUID? = id
        while let key = current, let folder = byID[key], seen.insert(key).inserted {
            result.append(folder)
            current = folder.folderID
        }
        return result.reversed()
    }

    /// The folder itself or the nearest one above it that is a subject (a "Fach"); nil when none is.
    public func nearestSubject(from id: UUID?) -> Subject? {
        guard let id else { return nil }
        return path(to: id).last { $0.isSubject }
    }

    /// Everything inside a folder, at any depth.
    public func descendantIDs(of id: UUID) -> Set<UUID> {
        var result: Set<UUID> = []
        var queue = [id]
        while let next = queue.popLast() {
            for child in folders where child.folderID == next && result.insert(child.id).inserted { queue.append(child.id) }
        }
        return result
    }

    /// A folder may go anywhere except into itself or into something inside it.
    public func canMove(_ id: UUID, into target: UUID?) -> Bool {
        guard let target else { return true }
        return target != id && !descendantIDs(of: id).contains(target)
    }

    /// "Schule / Mathe / Bruchrechnung", for a heading or a place to pick.
    public func pathTitle(of id: UUID?, separator: String = " / ") -> String {
        guard let id else { return "" }
        return path(to: id).map(\.name).joined(separator: separator)
    }

    /// Every folder with its depth, parents before children, for a flat list that shows the nesting.
    public func flattened() -> [(folder: Subject, depth: Int)] {
        var result: [(Subject, Int)] = []
        func walk(_ parent: UUID?, _ depth: Int, _ visited: Set<UUID>) {
            for child in children(of: parent) where !visited.contains(child.id) {
                result.append((child, depth))
                walk(child.id, depth + 1, visited.union([child.id]))
            }
        }
        walk(nil, 0, [])
        return result
    }
}
