<img width="1260" height="706" alt="Ryu, displayed in the RONIN model previewer." src="https://github.com/user-attachments/assets/57d1bbe1-ddf4-4dbe-82f5-a2d29033b37a" />
Ryu, displayed in the RONIN model previewer.

# Supported Games:
[Ninja Gaiden 4](https://teamninja-studio.com/ng4/us/)

# Supported AssetTypes:
- Texture (Preview)
- AtlasTextre (Preview)
- AnimationTexture (Preview)
- Font (Preview)
- SkeletalMesh/StaticMesh (Preview)
- AssetTable (Preview)

# Project Structure:
- **RONIN**, GUI app and general editor, contains all parsers used for previews, as well as GUI related stuff roughly following MVVM principles.
- **RONIN.Test**, a basic CLI application used to test features without the GUI
- **YakumoLib**, a library wrapper some Platinum Engine formats, for example, the asset system Ninja Gaiden 4 uses, as well as the general asset types.
- **DeflateSharp**, a wrapper for the DEFLATE compression library so it can be used in C#, if you are building yourself, ensure you have the static library at the correct directroy for it to build, or use a precompiled binary from a release, I doubt it'll ever change.

## NG4 Mod workflow

The prototype includes a read-only asset browser, GLB model workspaces, PNG/TGA texture sets, and verified `.ng4mod` v2 export. See [MODDING_GUIDE.md](MODDING_GUIDE.md) for the end-user workflow, package layout, supported boundaries, and verification commands. Third-party dependency terms are listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
