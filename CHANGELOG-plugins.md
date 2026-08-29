# Changelog

## [1.2.0](https://github.com/dknathalage/pane/compare/plugins-v1.1.1...plugins-v1.2.0) (2026-08-29)


### Features

* **files:** debounce file search + wildcard (* ?) support ([608cfe2](https://github.com/dknathalage/pane/commit/608cfe24cff3b6e8398819dff467a50e36f46052))
* **files:** debounce searches and support * / ? wildcards ([1d039f5](https://github.com/dknathalage/pane/commit/1d039f5de6b4ae8a5df0e2dcf9dc8defbc7197b2))

## [1.1.1](https://github.com/dknathalage/pane/compare/plugins-v1.1.0...plugins-v1.1.1) (2026-08-29)


### Bug Fixes

* plugin updates never clear — sync metadata version with catalog ([0a05b83](https://github.com/dknathalage/pane/commit/0a05b837694a4eb74dad0398c3abaafc3e2a08a9))
* **plugins:** keep plugin metadata version in sync with the catalog ([0bf2db8](https://github.com/dknathalage/pane/commit/0bf2db82ecc3474ffdeb9dc066e54256f89794a1))

## [1.1.0](https://github.com/dknathalage/pane/compare/plugins-v1.0.0...plugins-v1.1.0) (2026-08-29)


### Features

* **abstractions:** declarative plugin settings schema types ([5665a3a](https://github.com/dknathalage/pane/commit/5665a3ae62f7c19505d80b9535fa24cd975c051a))
* add PluginLoader.Load convenience wrapper ([2dfe6d2](https://github.com/dknathalage/pane/commit/2dfe6d2659e1b5e6017f7b2b2919d70cf4113ae3))
* collectible plugin load context and loader with shared-contract rule ([8f8b3f1](https://github.com/dknathalage/pane/commit/8f8b3f1a08aa9461259a843f7904beb9eb610ec5))
* fzf-style fuzzy matcher with scoring and match positions ([9a7affe](https://github.com/dknathalage/pane/commit/9a7affee1bd1981b27f96c60b9cc34522736a76b))
* integrate four plugins (flat src/Pane.Plugins.*) + ns2.0 compat ([58a1218](https://github.com/dknathalage/pane/commit/58a12183f388bd1a174e2b79134f35f43ebdb306))
* launcher UI with live query, keyboard nav, and match highlighting ([f506ecb](https://github.com/dknathalage/pane/commit/f506ecb4e0bdccf633016ea0d469524182a036ba))
* **macos:** Carbon global hotkey (no Accessibility) + menu-bar item; off-screen hide ([8ef131c](https://github.com/dknathalage/pane/commit/8ef131c3cbec94c9cbb96c467918413259e34dda))
* **marketplace:** default marketplace.json + DI wiring ([30e7d76](https://github.com/dknathalage/pane/commit/30e7d760bf112a7e52519a012c2b9a21beea15ee))
* **marketplace:** fetch/cache/aggregate catalog with install-state annotation ([c18f5d1](https://github.com/dknathalage/pane/commit/c18f5d1cd3b7faf7c702d964c703fc1925a64e8c))
* **marketplace:** install/update/uninstall orchestration + provenance ([d157820](https://github.com/dknathalage/pane/commit/d15782049d547f74ccd488e08d2e8433fab859fc))
* **marketplace:** installed.json provenance store ([76a0644](https://github.com/dknathalage/pane/commit/76a0644dd50b9ed9717ef9097f35232429a621ab))
* **marketplace:** marketplace.json models and parser ([99e36d8](https://github.com/dknathalage/pane/commit/99e36d877514df7ca7538ef1b841b228f8c2b6b0))
* **marketplace:** marketplaces.json config store with protected default ([3444537](https://github.com/dknathalage/pane/commit/3444537f38ced58e99b52405c253698559dbf0c8))
* **marketplace:** resolve git repo links to marketplace.json ([723c09c](https://github.com/dknathalage/pane/commit/723c09cb9effd6373158b46bb3b8f33c670cfe8f))
* Photino.Blazor host with DI, plugin load, and hotkey toggle ([0677e4d](https://github.com/dknathalage/pane/commit/0677e4de165814823cb6a13e172226c33a1357bd))
* platform hotkey/window abstractions and SharpHook global hotkey ([ed45e33](https://github.com/dknathalage/pane/commit/ed45e3304c902465d4fb90a7034ec584c86781db))
* plugin manager with lifecycle, silent error isolation, and unload ([494f567](https://github.com/dknathalage/pane/commit/494f5679126eb770b91cdaade0e0aa04c58dba97))
* **plugins:** files search plugin ([44644c6](https://github.com/dknathalage/pane/commit/44644c6f9d79f6eee54484b9e0c9e47cfbf81eda))
* **plugins:** install a plugin from a zip URL ([9dd0a28](https://github.com/dknathalage/pane/commit/9dd0a28ae664f6c7c8544d6157b6b4be8f675959))
* **plugins:** merge and deliver per-plugin settings into PluginContext ([6254f8e](https://github.com/dknathalage/pane/commit/6254f8e82f4fd1e57c8a8665da56c6a0f2b0afd3))
* **plugins:** on-demand update check and in-place update ([0c69633](https://github.com/dknathalage/pane/commit/0c69633c7a5618fbd819751f60703d7561b0f912))
* **plugins:** persist and apply edited per-plugin settings ([9f0b162](https://github.com/dknathalage/pane/commit/9f0b1621ff9ad80666d944506b96e478df587b4a))
* **plugins:** PluginFetcher downloads and extracts plugin zips ([fc0d7ac](https://github.com/dknathalage/pane/commit/fc0d7ac1c7b388d663688599cff5df610f9c674b))
* query dispatch with per-plugin isolation, routing, and ranking ([02cf471](https://github.com/dknathalage/pane/commit/02cf4716f7a7fed44d664408aa3725335ff8048d))
* settings page with plugins pane (enable/disable/install/uninstall) ([c1a9a95](https://github.com/dknathalage/pane/commit/c1a9a9588bc6e1cf6ec513bed596c5b203a45ffd))
* settings persistence and disabled-plugin state ([3c7a4b8](https://github.com/dknathalage/pane/commit/3c7a4b868c2315687cb8916ddb5669ef9161689a))
* **settings:** per-plugin settings field with backward-compatible load ([fe33b9d](https://github.com/dknathalage/pane/commit/fe33b9d9278d46c08a223075f3322b1f9652036c))
* solution scaffold and plugin contract types ([e35d7c7](https://github.com/dknathalage/pane/commit/e35d7c7e0c84fd38e0e9222f369d132108f0fbbd))
* spotlight behaviour, per-item icons, action hints, plugin-name search ([bcf7c58](https://github.com/dknathalage/pane/commit/bcf7c58f0a7d0b67d6942cd0ebadf44bcae313fd))
* **ui:** built-in commands surfaced in launcher results ([fd6f9b2](https://github.com/dknathalage/pane/commit/fd6f9b2001ba39afd94884b9612f04383ddda7ce))
* **ui:** marketplace tab — gallery, manage sources, git-repo-link add ([f7a03d7](https://github.com/dknathalage/pane/commit/f7a03d7ad40a6ec343caf724b7909e3ceb2fca9c))
* **ui:** per-plugin settings form in the plugins pane ([67c9c45](https://github.com/dknathalage/pane/commit/67c9c4576c275ea1b5f531f246a9b20e7a00e328))
* **ui:** Spotlight-style dark launcher with SVG icons ([9676d04](https://github.com/dknathalage/pane/commit/9676d042779d19eadff17b4a6948a878fa677414))
* **ui:** visible settings gear button + ⌘, shortcut ([f8c3a7e](https://github.com/dknathalage/pane/commit/f8c3a7e4622741a078364eb2aaacedc3b664e383))


### Bug Fixes

* apply fuzzy boundary bonus unconditionally ([e1ecdbf](https://github.com/dknathalage/pane/commit/e1ecdbf21b097b53b3eefbbc11271606fc544bbd))
* calculator unary-plus leaves operand unchanged ([e793eb6](https://github.com/dknathalage/pane/commit/e793eb6c91b7bc9583c5edf0acc268984a31290c))
* dispose launcher CTS, guard empty-result nav, drop template junk ([44ac881](https://github.com/dknathalage/pane/commit/44ac8811f7289a73101384667a3eb90653da8a2b))
* don't move window pre-Run on --startup (crash); start minimized instead ([c72d602](https://github.com/dknathalage/pane/commit/c72d602142e569be864c01c9fb1ac8f0c253fb2e))
* guarantee hotkey disposal, drain picker stderr, volatile visibility flag ([6dac826](https://github.com/dknathalage/pane/commit/6dac826b6892a42dd67ab7ae0a0a0f9f0f051b98))
* handle keys on launcher container so Escape exits settings ([85f97d7](https://github.com/dknathalage/pane/commit/85f97d7a91bc8b18aa8f66829abd78eeac7f5ebc))
* let Blazor autostart so the launcher boots ([bee79f8](https://github.com/dknathalage/pane/commit/bee79f8601093e92cd38d8796da73e2612bfa49a))
* **marketplace:** bump bundled plugin versions to 1.0.0; delete dead CSS; add regression tests ([1a345a3](https://github.com/dknathalage/pane/commit/1a345a3b5769cc5f3a9c99ae69f43f947f0221ff))
* Pane.App uses Razor SDK so wwwroot ships to output ([7c46f16](https://github.com/dknathalage/pane/commit/7c46f162168bbdf4277b31662e5e2239c3b9a8a5))
* plugin hardening — calculator unary precedence, ArgumentList launch, scripts SearchText, linux exec field-codes ([4255201](https://github.com/dknathalage/pane/commit/4255201ec36c657771a0de843a0b281cc70d32b3))
* plugin manager clears disabled-state on uninstall and rejects duplicate ids ([a1c7ad8](https://github.com/dknathalage/pane/commit/a1c7ad86b18abf84faf478b51b8f5dc29c67fbc2))
* **plugins:** validate+copy before unload in UpdateAsync; recursive CopyDir; ClearDir prune ([ec58f87](https://github.com/dknathalage/pane/commit/ec58f8732b72eb3b6bf241845460faa5e02118fa))
* raise fuzzy gap penalty so consecutive beats gapped-boundary matches ([e3c02b8](https://github.com/dknathalage/pane/commit/e3c02b8df17172dd0d6719391efddfe209995c83))
* run login agent via dotnet CLI + dll (apphost can't find runtime) ([1413e10](https://github.com/dknathalage/pane/commit/1413e101b6ffd7a4f7f1735e783a6428d5a3fa28))
* self-contained publish so login agent finds .NET; empty args to Photino ([925902e](https://github.com/dknathalage/pane/commit/925902e6838261aae869150568fe207ab9f2a4e7))
* start visible then hide after window is up (avoid pre-Run crash) ([6f424fe](https://github.com/dknathalage/pane/commit/6f424fe8c3fa66e0fb80daf514dc8c996da1bc16))
