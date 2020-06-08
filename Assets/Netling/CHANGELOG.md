# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Unreleased

## [0.3.0] - 2019-06-08
### Added
- A Sample that demonstrates networked objects, assets and remote procedure calls
### Changed
- Changed name of MufflonRPC to NetlingRPC 
- Asset management now stores ID in managers only (NetObjectManager and NetAssetManager),
  not on the assets (NetObject and NetAsset). This resolves serialization issues
- Renamed SyncTransform Jump to SetPosition

## [0.2.0] - 2019-06-05
### Added
- Automatic asset search on validation
### Changed
- Changed namespace from Networking to Netling
### Fixed
- Fixed SO singleton asset loading in tests

## [0.1.0] - 2019-06-05
### Added
- First draft version of Netling library

[Unreleased]: https://github.com/OJuergen/Netling/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/OJuergen/Netling/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/OJuergen/Netling/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/OJuergen/Netling/releases/tag/v0.1.0