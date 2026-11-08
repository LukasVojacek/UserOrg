## [6.8.4](https://opencode.it4i.eu/lexis-platform/backend/userorg/compare/v6.8.3...v6.8.4) (2026-06-23)

### **🔨 Refactor**

* **waldur:** migrate integration to Waldur.EFP module ([ebd0318](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/ebd0318c068b7a8d3d4ad77d1b93d8d848a99415))

### **🛠️ Fixes**

* HEAppE cluster-missing 500 + gateway 403 for system roles ([d2e99cf](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/d2e99cfaeaa19d6bee5715e0c734233b63118c52))
* **heappe:** match cluster by port-stripped login host ([#218](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/issues/218)) ([eb0de5f](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/eb0de5f9bf22341afae247feca27a85931d7fd3a))
* **heappe:** TemplatesInfo fast-fails on unavailable HEAppE instances ([#219](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/issues/219)) ([e05291c](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/e05291cfdebcd6afdbfc71e15795b62d62638c98))

## [6.8.3](https://opencode.it4i.eu/lexis-platform/backend/userorg/compare/v6.8.2...v6.8.3) (2026-06-23)

### **🛠️ Fixes**

* **heappe:** match cluster by port-stripped login host ([#218](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/issues/218)) ([46c2dbf](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/46c2dbf553173992ab3454e4ab1ddc1af5995cdb))

## [6.8.2](https://opencode.it4i.eu/lexis-platform/backend/userorg/compare/v6.8.1...v6.8.2) (2026-06-17)

### **🛠️ Fixes**

* **heappe:** prevent NRE in location partition sync for node types without aggregation ([2873970](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/28739702e51b414e703be9cccc1f437b9b037702))

## [6.8.1](https://opencode.it4i.eu/lexis-platform/backend/userorg/compare/v6.8.0...v6.8.1) (2026-06-15)

### **🧹 Chores**

* force LF line endings for shell scripts ([375d44d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/375d44d3cbeeccaffb7c8305724cbd06be18c4be))
* **apphost:** use containerized connections by default ([84be206](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/84be2060b864979804f229af9c03f90bc8ea2e0d))

### **🛠️ Fixes**

* **core:** make TemplatesInfo noCache query parameter optional ([9cb8c24](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/9cb8c243e4899d2528295ef9a71b50b71b89228c))
* **heappe:** cache HEAppE clusters per-cluster so a missing cluster is a cache miss ([48258be](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/48258be612686527c63080ee5d2eef4cabdde3d4))

## [6.8.0](https://opencode.it4i.eu/lexis-platform/backend/userorg/compare/v6.7.13...v6.8.0) (2026-06-15)

### **🔨 Refactor**

* **heappe:** dedicated health-probe client + harden timeout config ([f9c5ce3](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/f9c5ce31c4b7cf9f197874cb33f9b93ade89c9ea))

### **🚀 Features**

* **core:** noCache bypass for TemplatesInfo + configurable HEAppE HTTP timeouts ([c551ec1](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c551ec12b874cdec96667b2d0610dab5a5928142))

## [6.7.13](https://opencode.it4i.eu/lexis-platform/backend/userorg/compare/v6.7.12...v6.7.13) (2026-06-12)

### **🛠️ Fixes**

* **core:** evict empty user permissions cache on init ([07088b7](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/07088b7e51ed13e2f1ba3ab9d77441ccf92f83fa))

## [6.7.12](https://opencode.it4i.eu/lexis-platform/backend/userorg/compare/v6.7.11...v6.7.12) (2026-06-11)

### **🛠️ Fixes**

* **core:** propagate HEAppE cluster failures and return 503 on total TemplatesInfo outage ([93e180d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/93e180d124b755504d5d0f32e308c8196d773a86))

## [6.7.11](https://opencode.it4i.eu/lexis-platform/backend/userorg/compare/v6.7.10...v6.7.11) (2026-06-11)

### **🛠️ Fixes**

* **core:** wait out HEAppE hydration in TemplatesInfo instead of returning empty snapshot ([f931b9b](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/f931b9bdadc08dbf33407f2e635b1f35e83d021a))

## [6.7.10](https://opencode.it4i.eu/lexis-platform/backend/userorg/compare/v6.7.9...v6.7.10) (2026-06-10)

### **🛠️ Fixes**

* **core:** prevent serving invalid empty TemplatesInfo during degraded HEAppE rebuild ([c9649fd](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c9649fd621d9e0319ea497cd99205295dbef0384))

## [6.7.9](https://opencode.it4i.eu/lexis-platform/backend/userorg/compare/v6.7.8...v6.7.9) (2026-06-10)

### **🔨 Refactor**

* **rbac:** update permissions and endpoints mapping ([b4ab3e5](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/b4ab3e513d978bf4c7477abd7fc395028db43218))

### **🛠️ Fixes**

* **core:** prevent empty TemplatesInfo after login and dropped templates across HEAppE instances ([#214](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/issues/214)) ([ac348a5](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/ac348a5114ec9a0cbdacb6e03c00c2c543fd0099))

## [6.7.8](https://opencode.it4i.eu/lexis-platform/backend/userorg/compare/v6.7.7...v6.7.8) (2026-06-04)

### **🛠️ Fixes**

*  Better error message when project allocation expired ([e9d5088](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/e9d508888939422ede80834c7e17145a7bff597e))

## [6.7.7](https://opencode.it4i.eu/lexis-platform/backend/userorg/compare/v6.7.6...v6.7.7) (2026-06-04)

### **🛠️ Fixes**

* **core:** ignore soft-deleted assignments in resource-request guard ([#212](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/issues/212)) ([4186b28](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/4186b28178afc068a6cd9b100b9d2cf7e8b2f61e))
* **requests:** resolve 500 when accepting resource request ([#211](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/issues/211)) ([f660fd7](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/f660fd7c0a7d72a7ffa526a028d8150760f4fbde))

## [6.7.6](https://opencode.it4i.eu/lexis-platform/backend/userorg/compare/v6.7.5...v6.7.6) (2026-06-04)

### **🛠️ Fixes**

* extract method for fetching command template ([3117758](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/31177582678c83c87055a7f5ba00e77d5a29731e))

## [6.7.5](https://opencode.it4i.eu/lexis-platform/backend/userorg/compare/v6.7.4...v6.7.5) (2026-06-04)

### **🛠️ Fixes**

* Exception on heappeCmdTemplate null ([8233fb5](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/8233fb58747786874f0dbd7135c898a394132dcb))

## [6.7.4](https://opencode.it4i.eu/lexis-platform/backend/userorg/compare/v6.7.3...v6.7.4) (2026-06-03)

### **🛠️ Fixes**

* result-based command template retrieval ([e2f0360](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/e2f0360003702d90629b4e41ecfb968e3faa9286))

## [6.7.3](https://opencode.it4i.eu/lexis-platform/backend/userorg/compare/v6.7.2...v6.7.3) (2026-06-02)

### **🛠️ Fixes**

* use LocationPartitionEntry for template mapping ([5cd38cb](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/5cd38cb2647e4afbc269df7e35ef2d91bf27ff6e))

## [6.7.2](https://opencode.it4i.eu/lexis-platform/backend/userorg/compare/v6.7.1...v6.7.2) (2026-06-02)

### **🛠️ Fixes**

* improve template creation and partition sync ([0810242](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/081024298efa8c1f3ea0584fb3274174fb73e637))
* replace SKIP_SETCAP with --cap-add IPC_LOCK for Vault ([1db1e80](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/1db1e805801498094c3a4ba0696b08c627780ef2))

## [6.7.1](https://opencode.it4i.eu/lexis-platform/backend/userorg/compare/v6.7.0...v6.7.1) (2026-05-29)

### **🛠️ Fixes**

* centralize default template state logic ([4760454](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/476045431e625175bf0421da2e8f412242bb451b))
* fetch all cluster node type aggregations ([7a77eaf](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/7a77eaff07164936b5d93f9ed27236f80fa1501a))

## [6.7.0](https://opencode.it4i.eu/lexis-platform/backend/userorg/compare/v6.6.0...v6.7.0) (2026-05-28)

### **🚀 Features**

* **core:** update AssociatedProjectModel and mapping ([9e37ca4](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/9e37ca4ab07fb7ec9d571b316e6177656396c2bc))

### **🛠️ Fixes**

* update project key format in AssociatedProjectModel ([aba76b4](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/aba76b4781134d65f1af19120163eaf6e419804d))
* **migration:** automigration service added to forge ([33993d3](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/33993d34ba0f708c5a3546aeea596b4102e7d6df))

## [6.6.0](https://opencode.it4i.eu/lexis-platform/backend/userorg/compare/v6.5.1...v6.6.0) (2026-05-28)

### **⏩ Performance**

* **gateway:** cache permissions, tokens, provider ID; fix Cyclops socket leak ([0259f7e](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/0259f7e69dbf9f307054eb894bdb027d9513140d))

### **📚 Docs**

* reorganize documentation to match codebase structure ([78c499d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/78c499dd6750c901abdd24b70f6c89f2f4fcf2f9))
* **heappe:** add command template creation guide and fix response code ([7169ed3](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/7169ed37ab483976a3f2341438bd5e7bf1990256))

### **🔨 Refactor**

* **core:** optimize projections, error handling, and caching ([6f9ffa8](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/6f9ffa8fda8b6220945e8aabe9c4b21b2517464e))
* **core:** update default user role selection logic ([c2f98cb](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c2f98cb2822c4ab305f0a8a2f37ab01a882c27a1))
* **heappe:** clear cluster cache on template update ([e27f950](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/e27f9506a11b8c5736a74ef9be8f6ee89d96bf7b))
* **heappe:** remove HybridCache param from template create ([e030469](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/e030469631567d0864a00601660190e472756e18))
* **heappe:** require HybridCache in template creation ([cc25020](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/cc25020c939b2217b48468f24764999973630bae))
* **heappe:** use compact models for existing template tracking ([a12eb77](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/a12eb770aee77fd455c694566d67730b371ee5d0))

### **🧹 Chores**

* analyze multitenancy ([995313d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/995313d9cb7792c443b0c154e9643032a0442521))
* path typo ([d3a285d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/d3a285dd4d6635484e4543371ca3aaeac39ef65c))
* **rbac:** add commandTemplates and Partitions endpoints to RBAC ([8a0c035](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/8a0c035b334db856aa5eac955bb5fbb9734499ca))

### **🚀 Features**

* Extend-command-template-management-about-definition-for-specific-location-locationtype ([6a1e2d1](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/6a1e2d1cdcb885e509b32d98808c5092f53b4bae))
* **core:** add k6 Aspire resource for load testing ([3e18c04](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/3e18c047beef8e12a134c2cecc604aad5ee1aaff))
* **core:** add startup migration/seed services for EF Core ([1678100](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/1678100b449a2870c689f66233db16a27e3f20d1))
* **heappe:** add project cluster node type aggregation support [#177](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/issues/177) ([ea70431](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/ea704312761bca6dcadee5d84b375454e8c7bff7))
* **heappe:** extract BuildCommandTemplateRequest pipeline step and fix error handling ([1c10f49](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/1c10f49464a68f657d0cc8aa85e27e3308670acb))
* **heappe:** improve sync logic, logging, and config ([839008d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/839008d7737a4a2dee3f01da3d3975c807af7c79))

### **🚦 CI/CD**

* add promote stage to push images to EFP registry on main branch ([f29bd31](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/f29bd3169911b2ea7411c144f3a6fe0092ac45c1))
* **promote:** simplify image promotion using skopeo ([340a251](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/340a251066451c2412ceaa45fe7844071e949f1d))

### **🛠️ Fixes**

* bump up version ([eed06bd](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/eed06bd74468f5a7f0ac4b1965cb441ef34e4ed4))
* move OtelTrace middleware and fix CT usage in UserInfo ([ddf657a](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/ddf657ab734909dab90d4f57645b1a0b2da82d37))
* Partition synchronization ([f663eec](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/f663eecc40c4b2b30d57e9bc94093ce831206804))
* path typo ([15a7823](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/15a7823e4e87145d4d4593ce78909b1e46737706))
* swap to GetClusters method ([d7a4f61](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/d7a4f61ab8cb266c337dcc766e2619edfa20fdc1))
* **cacheWarmer:**  use SafeFireAndForget ([26015be](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/26015bebb8325d2d5e57ec4f93259a146b967a60))
* **heappe:** ensure TemplatesInfo cache tag is case-insensitive ([f434eb4](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/f434eb433ba0c64b7dd74dc4732022ec13ce1191))
* **heappe:** group links by URL/resource, improve logging ([2da0d0a](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/2da0d0ae2eace57babebe55499a320256d168db8))
* **heappe:** invalidate TemplatesInfo cache after CreateProjectCommandTemplate ([3bcb873](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/3bcb873f8e780706d6c9c80f1ea887b28217d507))
* **heappe:** return 201 Created instead of 202 Accepted from CreateTemplateEndpoint ([d4ecb33](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/d4ecb330e602498b9fdb7dd23cdae7475c363d12))
* **heappe:** set circuit breaker sampling duration ([10d009d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/10d009d539bca13e7ec25ae64e24812af7f7aa98))
* **heappe:** support CreateModel in Scenario 3 of command template creation ([c0d9217](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c0d92171b76ffeac3451523d2f0b6697dd52b8de))
* **heappe:** switch to ServiceTokenHeappeConnectionInfo ([8da6207](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/8da6207fa8cdc114431f47228da2ff5d9b9081c0))
* **loadtest:** check PascalCase JSON keys in responses ([4a51b75](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/4a51b7578ee2884d8c8e6690126cad676e38b50b))
* **templates:** synchronization & static templates ([da97a60](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/da97a60f87fc73ac4bb523277cc86c22b412e7b6))
* **test:** update sync test setup, remove unused test ([dc449ca](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/dc449ca31f4cd569417aec999f48d18784819027))
* **userinfo:** missing system permission ([e873f0a](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/e873f0af9b2b3a2c8f181d9830642ab102008837))
* **waldur-sync:** add per-item log scopes and fix trace hierarchy ([c963ea1](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c963ea142dcb858b20d0086f460ced5c6f8d10ae))

## [6.5.1](https://opencode.it4i.eu/lexis-platform/backend/userorg/compare/v6.5.0...v6.5.1) (2026-05-27)

### **🛠️ Fixes**

* **templates:** synchronization & static templates ([845e87c](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/845e87cffb07a0843c4fdee52458ea58729a48f2))

## [6.5.0](https://opencode.it4i.eu/lexis-platform/backend/userorg/compare/v6.4.2...v6.5.0) (2026-05-22)

### **🚀 Features**

* cache optimilizations for HEAppE related hotpaths ([3bb71a4](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/3bb71a4411457ca224ca7077fe87f3bfa744427d))
* **core:** add admin cache reset endpoint and tests ([42a510d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/42a510d84544cff8b8419aa488c96a458cd76aa3))

### **🚥 Tests**

* **heappe:** add version and scenario tests for HEAppE 6.4+ API ([3391335](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/33913357b403197de83a971a9004ea148d8065c9)), closes [#193](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/issues/193)

### **🛠️ Fixes**

* add missing AsSplitQuery to EF queries with multiple Includes ([e9b3d93](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/e9b3d9352cc33e1c32b2a808b3a85a204aab16da))
* add missing AsSplitQuery to EF queries with multiple Includes ([91b4451](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/91b44513b3b0cb6d5ccff272293b0261f00943a8))
* **gateway:** avoid Activity.Start for traceparent header ([8e11a3a](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/8e11a3aef08ac43a164800d4b1b44f2e6cf069c8))
* **heappe:** use IgnoreQueryFilters on partition state update query ([a1f12b0](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/a1f12b02556c61102eb408d9303ef184704e084c))
* **heappe:** validate heappe version before use ([490ea27](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/490ea2732bfbac3e4dda2896a608d13861b3d572))

## [6.4.2](https://opencode.it4i.eu/lexis-platform/backend/userorg/compare/v6.4.1...v6.4.2) (2026-05-22)

### **🛠️ Fixes**

* missing querysplit ([7a7278e](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/7a7278eafb88cd21c7fec30a0442a73d7744286b))

## [6.4.1](https://opencode.it4i.eu/lexis-platform/backend/userorg/compare/v6.4.0...v6.4.1) (2026-05-20)

### **🛠️ Fixes**

* **Waldur:** Deucalion storage handling ([8fe3b9d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/8fe3b9dc39e3d761023e688062fdd9a3519b64f0))

## [6.4.0](https://opencode.it4i.eu/lexis-platform/backend/userorg/compare/v6.3.0...v6.4.0) (2026-05-13)

### **🚀 Features**

* Parsing EffectiveId in Waldur Integration ([456f833](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/456f8335b602e19c9feb9181a55eca61ebe697f1))

## [6.3.0](https://opencode.it4i.eu/lexis-platform/backend/userorg/compare/v6.2.0...v6.3.0) (2026-05-11)

### **🚀 Features**

* **waldur:** add iteration-level DB mutation tracking for debugging ([be593f4](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/be593f4219f4807ac61f740809b0ebc248fcf7cb))

### **🛠️ Fixes**

* Eager-load navigation properties in user context queries ([6b533d8](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/6b533d88d80b1492608978d9b208d25ba2213e82))
* filter assigned locations by HPC type and null-safety ([ddd4b27](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/ddd4b27fef97386cafee4f226798422b4228a13a))
* **heappe:** truncate project descriptions ([091303a](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/091303a96c751503425af109e83671f8bedd44bf))
* **logs:** reduce logs - mvoe to debug level ([e1cab53](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/e1cab532ef868d3f375422fa4c42f23beea0152f))
* **waldur-integrations:** replace exception throws with structured logging ([061e73e](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/061e73e50dec17d895181165b957634ec4884d5a))
* **waldur:** add debug logging to project producer ([aaf74ae](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/aaf74aee8f28193854e19fabbbc8169c1d1d902a))
* **waldur:** harden sync persistence and failure paths ([d3ee565](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/d3ee5659df86d6333204fc7368f4bd687233cae6))
* **waldur:** prevent DbContext tracking conflict in join entities ([5277caf](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/5277cafbbf9a349b715d27e7b4e4acda01fa6838))
* **waldur:** prevent duplicate reference inserts ([9630e6e](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/9630e6e8ebd1fda77006ac38ff41e2f71424a9c2))
* **waldur:** stabilize export entity tracking ([c630e44](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c630e44f4026648438d6c5c789d7aa9f02d83db1))

## [6.2.0](https://opencode.it4i.eu/lexis-platform/backend/userorg/compare/v6.1.0...v6.2.0) (2026-05-06)

### **🔨 Refactor**

* add batch add/remove to context interface ([9b141ba](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/9b141ba9f0f8aa55dd04cb9dc95caa45282fbc5f))

### **🧹 Chores**

* add missing env to aspire [skip ci] ([0216b55](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/0216b5534eff92b405dbb578fc80f1f8616dea5d))
* ignore SQL script artifacts ([7c107ff](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/7c107ff0e6226ffbc8cf12e2efb046640ef92fc0))

### **🚀 Features**

* **api:** implement ReadOnly Mode feature flag ([51dfdd3](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/51dfdd301917515eab5324690100be2cf1cac71f))
* **gateway:** add dynamic customer ID lookup to Cyclops forwarding ([820f7b0](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/820f7b0de6727c54ddc3f0ef2a06c708569df9f5))
* **gateway:** add dynamic customer ID lookup to Cyclops forwarding ([c09f053](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c09f053c38196cf4218bf80e6a162552512be85d))
* **heappe:** implement preferred authentication type support ([c2746b4](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c2746b43a60c2c74856784afee7f274bc5250fc6)), closes [#193](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/issues/193)
* **openapi:** add Service token authentication scheme ([d1ad182](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/d1ad18209d5310d7fac71c4694f12a3b3d62eaf5))
* **sync:** enhance AssignedLocation SyncFrom logic ([bf080f5](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/bf080f55bdfce2b80ee831ff609aa6786e9860bc))
* **waldur-export:** add config-driven shared provider logic ([f825bc3](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/f825bc3f23bba16cd1ce8ad11921c2dbf1831494))
* **waldur:** add debug logging to import pipeline ([163d972](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/163d972ca6d71480128c05055ae9de2c46578438))

### **🛠️ Fixes**

*  HEAppE authType ([c30ce5c](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c30ce5cd6c333dc80fca00c470ea9cb69284344b))
* **api:** change ServiceEndpoint response type from IList to List ([de6f486](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/de6f486f06c9291ade825c9322327df4a71947d4))
* **api:** mask sensitive headers in request logs ([912ef64](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/912ef643f2f48bcd925c77f9df74a422c85d4025)), closes [#197](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/issues/197)
* **EnsureProjectResourceAssignmentAsync:** simplify ef relations ([63958fc](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/63958fcddf7b855732400d9d1d909b98c3e9d176))
* **waldur:** resolve EF Core duplicate tracking in storage locations ([eaeeb67](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/eaeeb67b92bba245dac57d18c2e72fd1787eca9a))
* **waldursync:** missing systemPermission for new users ([74a67b4](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/74a67b42a7e336cd9fc3d0de291ff190843ce0e1))

## [6.1.0](https://opencode.it4i.eu/lexis-platform/backend/userorg/compare/v6.0.0...v6.1.0) (2026-04-26)

### **📚 Docs**

* **commit:** update agent docs, default to local DB ([7f7836f](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/7f7836ff1bba2f35f6d4a68ca0b93b5bf1ad1009))
* **ddd-migration:** reorganize documentation with central README ([ad5f440](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/ad5f440f765cfba68cdab9e907823d5c68b95d48))

### **🔨 Refactor**

* **auth:** unify username handling and lookup logic ([933e463](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/933e4631fe8e6447786314e13b2be735d27bc4c6))

### **🧹 Chores**

* **nuget:** Update NuGet packages and add OpenTelemetry.Api refs ([32d6801](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/32d6801a1958ea044a362d4e24e833896c31d294))

### **🚀 Features**

* new endpoints ([caf1ebe](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/caf1ebee122e084d3a17d0d37e239cf8a8dd7644))
* RBAC for endpoints ([2948af5](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/2948af5c71e30cd5146d12175da474978c30e1b6))
* **api:** migrate Swagger UI to Scalar and update OpenAPI ([1fb339f](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/1fb339f5252850c06356b78b1244a79d6964e68f)), closes [#180](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/issues/180)
* **apphost:** make Idp and UserorgDev parameters conditional ([53489da](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/53489dae1e070d9c267c94415b6683f3eb757fcd))
* **rbac:** add usage report permission to Member role ([1b29b30](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/1b29b3018f39237bd5a236a469dcb4fa0c85bdcb))

### **🛠️ Fixes**

* ensure Vault and DLQ reliability ([6ca6d36](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/6ca6d366e778b1d09ea83f51285232453c9ddd2e))
* userMapping problem ([c693d6c](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c693d6ca576c9895a9318c1849b23c5b2a33e590))
* **auth:** support alternate preferredUsername claim ([072b091](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/072b09197f5fc6ffe6698274486c01d5c1b78848))
* **auth:** update OIDC claim handling and user sync ([26961ed](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/26961ed71e984b237fddec823b6f97ad056020b9))
* **core:** fingerprint UserInfo cache ([c2e9bc9](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c2e9bc9d4074fdfacf16a96318a251b063b50147))
* **gateway:** improve error handling in internal token middleware ([568a6e6](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/568a6e6f33a2ce7efa13b83055bcec02056a8e6f))
* **permissions:** use case-insensitive aggregation match ([8b68ed3](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/8b68ed34a2bb0d06aff37728fe3a0804745d2aa7))
* **repo:** improve error handling for user retrieval ([d2fe107](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/d2fe107ab0b274da7ac0fe97fd2b9ac9ef53b8fe))
* **role:** persist role import permissions ([5b6f0cb](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/5b6f0cbe3f8d22927787f4ae55ec5efedbb27be4))
* **tests:** update member role tests to expect forbidden ([54c18d8](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/54c18d82301b82825943c8afbcf57c77b2eb4f13))

## [6.0.0](https://opencode.it4i.eu/lexis-platform/backend/userorg/compare/v5.1.0...v6.0.0) (2026-04-07)

### ⚠ BREAKING CHANGES

* update cluster spec naming and structure
* **IWaldurCollectorService:** update interface methods
* **core:** add operation mode, external id, cleanup
* remove history tracking tables and code
* **persistence:** 

### **👷 Build**

* add AsyncAwaitBestPractices dependency and bump Microsoft.Extensions.Hosting.Abstractions to 10.0.4 ([0e886ea](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/0e886ea80e8b0561401ff0f122ac3f05581794eb))
* **Dockerfile:** copy RBAC seed file for publish output ([671cd77](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/671cd772e187b290cd5801388e5377b98cd76cc2))

### **👾 Dependencies**

* update Aspire.Hosting.Testing to v13.2.0 ([676814a](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/676814a5f66aad751c2a55d04b109171fcc9c635))
* update Microsoft.Extensions packages and DTOs ([6f02794](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/6f0279496b373c047a3e65daa2b95a8735a99a68))
* update NuGet packages to latest patch versions ([de2977a](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/de2977ae8886d27aab14afb587c65cba675b4f72))
* update packages, tracing, and logging patterns ([1681a0a](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/1681a0aa05ae7fa9717917b5ce48ee1be505fab1))
* update StepUpLogging and add HashiCorpVault ref ([59a922b](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/59a922b9546a86133830709256a11dc1e1c7b31e))
* **logging:** update StepUpLogging and enable summaries ([161dde9](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/161dde9b7f856accd8e853f3cfef3c438db2b663))
* **logging:** update StepUpLogging and log summaries ([fd5153c](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/fd5153cffd5ea4307d97410906e1946577dc653a))
* **nuget:** bump microsoft.extensions versions ([ad34972](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/ad3497250b7bf2647e86d74da077e95c077744f0))

### **📚 Docs**

* add design doc for background periodic sync ([ad53a3b](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/ad53a3b9cdaf2e1860e7ef380ae8fdf2cacd5a80))
* add detailed Waldur sync sequence documentation ([ea62d04](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/ea62d04f3adddd012f140476352ae0c45c40ca4b))
* translate and standardize terminology in synchronization sequence documentation ([576419b](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/576419b243189a9eb11d179bec382eaae105e0eb))
* **api:** update request state docs in OpenAPI ([14c7d84](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/14c7d8496d765956e550e73c42bcd42658f93b80))
* **aspire:** update skill guide and enable MCP agent ([689ec34](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/689ec3461f6dd5849be7f4cacc9c4fba2e0584a3))
* **waldur:** add comprehensive documentation for integration flow and diagrams ([c10a54b](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c10a54b6e9984cd31c24e89a0edc6f75bb08fd3e))
* **waldur:** add detailed sequence diagram for sync process ([5d99287](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/5d9928720861a2e3329d03a4c6e981de21415e04))
* **waldur:** enhance sequence diagram for synchronization flow ([c3405bf](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c3405bfb602a7f0dfd6e45b6c1510fb98c637b5f))
* **waldur:** update and clarify integration docs ([5a29a28](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/5a29a2813ae4848f93b8f193aefa4bada9c478e8))
* **waldur:** update diagrams and clarify sync details ([06bda39](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/06bda39ca34163b196dff6e8e1c13f20aca78246))

### **🔨 Refactor**

* centralize HEAppE contracts in new project ([f335dc6](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/f335dc613565d845bb80b358ef9eff19556ca0e9))
* reformat tests and clean up comments ([da6b6dd](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/da6b6dd43bb57c4b3485dda835f00f726c5c186a))
* remove Vault endpoint and stub offering fetch ([ef0c441](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/ef0c4415b8a76003c8138fb1370f308bb76005aa))
* rename WaldurResoruce to WaldurResource ([270e5ab](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/270e5abf6184984e85fb84d1c7de3ec869ac44bf))
* use hyphen-less GUIDs in API requests ([bc03b22](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/bc03b2227daa4ab412eb497ffd3073e7fdf60ee9))
* remove history tracking tables and code ([4a690d6](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/4a690d64070df92c387ddc131ab3c9780d23c698))
* update cluster spec naming and structure ([6d0f1e3](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/6d0f1e364e771304ee83621830a931b632516523))
* **api:** improve Cyclops credentials handling ([71c286d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/71c286dcbae0bec4c9c38e865529d6ba9b4f831a))
* **api:** simplify Cyclops proxy endpoint logic ([058c680](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/058c6802bf5d619fc8a5549d8c1726091814aaae))
* **api:** split DTOs into Requests and Responses ([3fec75f](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/3fec75f01d642227294a0739c52d21fad2dd2c85))
* **apphost,tests:** update env structure, simplify test ([405902a](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/405902abba3046e81aae843ae0578a0f1d7bb3f3))
* **apphost:** clean up env vars and logging config ([d574fa1](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/d574fa10eaa487834a35944fc8f161347625c75d))
* **apphost:** modularize infra service setup ([5a7cd50](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/5a7cd508c412779d4737a0e8e4e4279537819415))
* **background:** move export user init, enrich HEAPPE ([d85cb65](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/d85cb65dbcf233f1cd62f70dc9e602c2cea72aff))
* **constants:** centralize HEAPPE keys in WaldurConstants ([8117743](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/81177437bc61d9ed87c1e011b1b6328393555793))
* **constants:** centralize Waldur state strings ([018e3aa](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/018e3aacd4309aa5b8ce35fd985455fd0da7b8cd))
* **constants:** update project domain name to External ([fd7853f](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/fd7853fae019241f7f277e237a1e1ab664876574))
* **contract:** reorganize HEAppE contract namespaces ([d71e6d9](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/d71e6d95b8a6a44c0d3ca393b887bf5d3c02d8a4))
* **contracts:** extract vault contracts to new project ([774b811](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/774b811c5431891fbd1d039933840a45e9261736))
* **core:** implement two-phase pipeline orchestration ([e80b01f](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/e80b01f19975471376dd200705085892b6d26f9c))
* **core:** improve EF context handling and error logging ([5903993](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/590399309cd6e357d178d7afbdae14969fb7017d))
* **core:** improve resource handling and validation ([9cb48ee](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/9cb48eec5398f07e0522cccbabb59467384f7363))
* **core:** unify DTOs and processing pipelines ([50bb290](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/50bb290b4e67256ff6095e0e0c91d9f24138b664))
* **core:** add operation mode, external id, cleanup ([550cb31](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/550cb315d61bce7d99455c8810f5a4cf5beb8b52))
* **db:** centralize EF auditing and soft delete config ([a61d749](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/a61d749ab423c86f3de24cd26a275aaa73b8a622))
* **db:** rename Project PK and reduce Data length ([5115c8c](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/5115c8c8d430b1c9a88e2e12f6d649cdba3076f9)), closes [#127](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/issues/127)
* **domain:** standardize audit and config for entities ([fea4979](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/fea4979b2f5327c01d9c2e0d11e6c730523b392c))
* **domain:** update audit and service integration model ([24d30a4](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/24d30a43d2aa0bf7abc5792f816d811438286aef))
* **export:** improve location name extraction logic ([7a98fd0](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/7a98fd02d3d2054911142b5ad0aab90419811a99))
* **gateway:** use bindconfiguration for options ([40214d4](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/40214d4d6b130744e1bbeb6c03a448d743a2b704))
* **hostcontracts:** unify exporter contract return type ([4b97550](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/4b975508c04735b9eb0ceb07b453791a2cdd7a98))
* **init:** modernize HEAppE project initialization ([0bca14f](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/0bca14f18f70caee74d6a6e6cc5b0caa65c6634e))
* **init:** update namespaces and prep HEAppE integration ([46fda87](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/46fda87e95a82ff443c036875ceeb33c3ad05b42))
* **IWaldurCollectorService:** update interface methods ([380e9ad](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/380e9ad8d82f33d8774c7b36b9cbd5aa39a71a5f))
* **logging:** adjust log levels for consumer events ([3369792](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/33697923932339b532e6f5f927165fb6e1f84181))
* **logging:** migrate to Microsoft.Extensions.Logging ([8edc458](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/8edc458b283e7f0c3a424769e8e9523bd05348af))
* **logging:** use source-generated log methods ([8e26c53](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/8e26c53defda52d443d16c845ea7a4886dd6e143))
* **modules:** modularize HEAppE integration structure ([4de5315](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/4de531572754b35694ca7ab2f9b379fe993bd299))
* **operation-mode:** domain/response enum separation ([c60f108](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c60f108b7acc5e127a1ce17fd5dbedc75356718c)), closes [#72](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/issues/72)
* **orchestrator:** disable processing pipeline logic ([b57997f](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/b57997fc68079ea883a404b8c4c145317b47774a))
* **orchestrator:** extract entity helpers for clarity ([036199d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/036199db2df7f2c3a7f1bf6e38f6e94e9181e78b))
* **persistence:** reorganize base entities and interfaces ([5768fe9](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/5768fe96a930f81bca51c6009084b666dac4d609)), closes [#127](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/issues/127)
* **pipeline:** process providers before projects ([5cb71ab](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/5cb71ab7c3993c4215690b9e6175130885640ee3))
* **rbac:** remove project resource assignment perm ([fc23dd5](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/fc23dd56823242dd8468af304561575ac0da0dd6))
* **secrets:** Retrieve Cyclops API key from Vault ([c119ff0](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c119ff00ac9d86043bbcbaba386426ed6d1e757b))
* **shared:** merge Persistance into Shared project ([d2f4f63](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/d2f4f63290180df68f4f17d7faee4ef1f13c9c38))
* **startup:** modularize gateway forwarding logic ([105bc9e](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/105bc9eec3c10b343a66de676a4ffbf37c07cb46))
* **stomp:** migrate to WebsocketStompClient lib ([c90bd0e](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c90bd0e6820aacf111aaf32e28e46250311804d4))
* **stomp:** update RabbitMQ endpoint mappings ([9720ee0](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/9720ee010579e8d0da3ad88308e2678e1fffeaf7))
* **sync:** clarify error for missing locations ([08b87c7](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/08b87c7b9f0e75f8a004c9c0ba2bd598cb598d8a))
* **sync:** remove priority queue, use per-iteration registry ([8d6df3b](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/8d6df3bdeb420f78fbf08a872b9a44af4863b34b))
* **telemetry:** migrate to AddException for Activity ([1d5ab15](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/1d5ab1573bcf606c09a484bef61bbbc7e11db330))
* **telemetry:** unify activity and metric reporting ([4d7203a](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/4d7203a8717ba3593f603e25fe8179b900eefb9e))
* **validator:** improve validation and add docs ([8a430f5](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/8a430f5b75c054c4722a7df9bf04241c99cf8213))
* **waldur:** batch user export and orchestrator API ([bbe39b7](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/bbe39b7f76f347ba31f97b2880d8fbe067a0b2e9))
* **waldur:** finalize processing pipeline wiring and DI registrations ([cb238a0](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/cb238a05d815ef3c9cc230747bf5adbf757eea31))
* **waldur:** improve DI registration and options ([ff08b2d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/ff08b2df7ebda2667dbcf202f338b9fb78e0b4e4))
* **waldur:** improve role mapping and code quality ([0ff96d8](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/0ff96d8acac7fcfc775bbc1ae51ad3f2af022772))
* **waldur:** modernize streaming API and interface ([3909bb0](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/3909bb099c2569f02711c0a9d5c9d1ee434ca929))
* **waldur:** modularize export orchestration logic ([cdc272b](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/cdc272b969b9d9d2716cff993b9c7ed7f12ea209))
* **waldur:** modularize exporter and map domain entities ([7d2bf07](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/7d2bf07b282289756e25dda8a41e4eb27aaaf808))
* **waldur:** modularize pipeline for multi-integration ([3320546](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/3320546b1b7517054cf328dd8d18de4b4f209115))
* **waldur:** move orchestrator to new namespace ([60ae61f](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/60ae61f4a85f7f5f249ee9a781e6bf7f9d4ac8c3))
* **waldur:** rename HostContracts to Contracts ([9454d92](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/9454d92a109e1ce01a48466315389124bf14015d))
* **waldur:** reorganize BackgroundPeriodicSync into domain-specific subdirectories ([73f0571](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/73f0571911fa364e9088c4ca78017476a47ad5b6))
* **waldur:** simplify async stream return types in IWaldurCollectorService ([c07b2ec](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c07b2ec6fbdacfe9fef103c86cef83d80e5e6ad5))
* **waldur:** simplify integration and endpoint config ([9b0af2e](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/9b0af2ebc8f0104a48c83dea343ffe823f3c6139))
* **waldur:** use IOptions for DI and config ([cf79878](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/cf79878f641bf0357c38f2c9f46f26a249ec6b79))
* **websocket:** refactor stomp client registration ([c0c4393](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c0c4393746b8d46955e44fe528d5a8659d2af16a))

### **🧹 Chores**

* cleanup code - dotnet format ([d15b6ea](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/d15b6eaae3db81b5d70ba26834137442b295c9a7))
* code cleanup ([94af19f](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/94af19f51bf755784e66f4b67c5eea832c3ac822))
* **Adapter:** move HeappeAdapter to Userorg.Adapter ([b777a8d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/b777a8dd9bcf04f60703c915a45e6ba9b638d4c3))
* **apphost:** comment out STOMP server for release ([0e0e168](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/0e0e168eaefc271e84baac9097fa255b659c1554))
* **apphost:** enable local db by default ([66ae95e](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/66ae95e252368b23dbf47b7de4cfdb0caa8209da))
* **migrations:** rename migration files for compatibility ([9a1e7f7](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/9a1e7f7849fdce978599e9e0394ce517d6fd13cb))

### **🚀 Features**

* improve cluster validation and endpoint handling ([b46be9d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/b46be9d8d61947110e5eba37ce0ad7edfebff831))
* test method for STOMP messaging ([142c623](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/142c623e79d123cd84070cb533bf9409bfd878ed))
* **adapter:** add waldur exporter and orchestrator ([59ecebc](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/59ecebc538347c581adb00db69964cb8235ad436))
* **api:** add Cyclops usage proxy endpoint ([4d149b1](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/4d149b1b7a10abfc23e3550ebbb55c9a5d609bf9))
* **auth:** add permission check transform for YARP ([12b227f](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/12b227f50a380c3084f26a78eb9c4c579b629f4d))
* **auth:** add project-level permission checks ([fe0ce36](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/fe0ce36b5ef7b3928f6bc3dc0afbbb8145004010))
* **auth:** add route-value substitution for permissions ([68a87ba](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/68a87baf2ab3d367cac98c145b4c52516060b5f9))
* **background-sync:** pipeline-based orchestration ([63a3fae](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/63a3fae0abab9abcf2ac195826d94de5805d2747))
* **config:** add vault secrets mapping ([6e12bde](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/6e12bde9cd538267bc06f70b2dda500a09ad9eff))
* **config:** rename Vyvlops URL to Cyclops URL ([be2cd35](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/be2cd353c58bc85a0b96470ebed4cd6b3367c9d0))
* **core:** add AllocationUnitMapping for conversions ([7168937](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/716893769fda02376b67a2bfd7680283e5bf5045))
* **core:** add configurable parallelism to consumer ([4820a4d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/4820a4d64248083dbeac6e67aee4924e5cc34f69))
* **core:** add detailed cluster model and project setup ([c385773](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c385773fbc182b9a85be23b571623cd29d59105b))
* **core:** add GuidNoDashJsonConverter and update DI ([115f7c3](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/115f7c3e4b00f978b4d71e38a5522a18b26eb610))
* **core:** add ID and state validation for DTOs ([3e4bfe0](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/3e4bfe0a6b6c6e6552158b2c5b2a0611a32fdf78))
* **core:** add IUserOrgDbContext and Waldur export refactor ([6c08c93](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/6c08c934a5ca32d4104c1382523c4e9d150ef619))
* **core:** add role assignment filtering logic ([bed61cb](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/bed61cb7927427b80f6f863d9ff57f5929698416))
* **core:** enforce read-only record protection ([23e8586](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/23e85861ea4f09ef382f3a9140b398b319bc17e4))
* **core:** improve entity sync and allocation handling ([237ce12](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/237ce12c8e8ea80e44c0d0f08318219ed8b2fa65))
* **core:** increase Provider.Description max length ([7aa79d5](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/7aa79d5c9393eac8bdcb8285e170664dabcc4c86))
* **core:** register IUserOrgDbContext and seal orchestrator ([e10728b](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/e10728b727a143cfa1ed85e858c27b356f3635cd))
* **core:** support decimal allocations and desc truncation ([c016f1f](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c016f1f6c8370352c9e0e263dc751b95c9dcc5e2))
* **cyclops-forwarding:** update query param names ([ac03e26](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/ac03e2681fd86fe51a4da41e6719c95d1866af06))
* **data-collector:** add strongly-typed Waldur interface ([d63d389](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/d63d389ac0e660aefcb858ba3033a0dac0f55027))
* **db:** add external integration and mode columns ([c1ff7f8](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c1ff7f889d73ddd9557b7501807c5aa48c17424d))
* **domain:** add ELocationType and propagate usage ([c1b1097](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c1b10972662d3c2af67ac35c6f4631282fdcfb4a))
* **domain:** add UserProjectProjectResourceAccess entity ([94a3ed4](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/94a3ed4adb796857f870a6e904d4ea5da1139bbe)), closes [#127](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/issues/127)
* **exporter:** implement partial project export logic ([4b6106a](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/4b6106a222039e948a54e961f09e84340da3fed8))
* **exporter:** update project export to resources ([1eb71b8](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/1eb71b8774817707de4c01e1a5cadbb98ed8c4ef))
* **gateway:** add CyclopsOptions and refactor config ([e6275af](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/e6275af02017781da03d3dfaae81c10d2d5d5ea8))
* **gateway:** add RBAC seed hosted service ([3e500c5](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/3e500c59300a48e016ecd636773c914128a736d7))
* **gateway:** make API key auth timeout configurable ([f12b5d0](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/f12b5d0637837d3cbdad23858145bab82050e1aa))
* **heappe:** add database seeding and initializer svc ([5f2adb9](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/5f2adb9a11f00e31842650fc67fb26d65cef3a66))
* **heappe:** support updating project-cluster assignment ([f756136](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/f756136d2b36cc1c9859c2924ba8875af4a01b55))
* **heappe:** use EUsageType and centralize config keys ([970ff3d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/970ff3d573223a06df0cf19b4701ec4f38b8a9e8))
* **http,background:** add resilience, rate limiting, pipeline ([892f3f6](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/892f3f6562a660e6924fe15b295dc1eaebfa1e2c))
* **integration:** enforce config validation and opt-in ([046f738](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/046f738bd7e804374c26b127573299083478f78c))
* **logging:** add structured import failure logging ([627285e](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/627285ef305ee45f37f45fddb200c2b39bfdfcc5))
* **logging:** add structured logging for pipeline ops ([87f114a](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/87f114a228bf82b25e7ea48c1b0cc0e29bad93aa))
* **metrics:** record counters on each channel write ([59d8162](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/59d8162c39479259358ec9f8944ced0be1daf4fa))
* **migration:** add ExternalId, Mode, ServiceName columns ([8be735f](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/8be735f32dca69d3b12e8a7edb76ee53d65cd176))
* **orchestrator:** handle HEAppE init with local base path ([4817571](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/481757148353bf03e85891c464c92f87501da862))
* **project:** skip resources missing BackendId with log ([a44f95e](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/a44f95ef511b29db99ecf9d6102cf548d89b506a))
* **rbac:** improve endpointMappings structure and add APIs ([ad2684b](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/ad2684ba7285e328c768f3c5bd4ae19ec09ac316))
* **rbac:** update permissions and endpoints ([ea5119e](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/ea5119e0899da110b186eb2c34d14bfa4b000081))
* **rbac:** update permissions and endpoints for Cyclops ([9a7ef0a](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/9a7ef0ad5d376170698352cdfbc8d8ddda081e7a))
* **stomp:** add integration tests and improve client ([fb2f0de](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/fb2f0ded05eafdbb585dabbbcb28840a5227e5d0))
* **stomp:** add Waldur.StompClient library ([24778f8](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/24778f8a7e4cf132296d62d768af20be9868c716))
* **sync:** add channel depth telemetry and metrics ([8febd4d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/8febd4d0e6502438583fbe23280d7f835069ed35))
* **sync:** add DLQ for invalid provider country ([2f3f091](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/2f3f0911e46f3c5eaab1bbafebf4dd614abc363d))
* **sync:** add project skip registry and skip logic ([0a0f1f9](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/0a0f1f93e4839ffa15efb7ed79d113cb8c6ad815))
* **sync:** improve tracing, error handling, and tests ([c7914c2](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c7914c212c0cdd03c0428903db2667363ae2513d))
* **telemetry:** add child activity tracing for fetch ops ([ca3de22](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/ca3de220999dc9da117e2587b1df0cc003f896f9))
* **telemetry:** add per-item activity spans and tags ([70e4607](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/70e4607a3b640f637b767e8513d40b39555f5577))
* **test:** add optional params to WaldurDto builders ([ed9b0c5](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/ed9b0c58feb7bd9eaef32ff6212e305eed09ec9d))
* **tests:** add FakeServiceScopeFactory for testing ([4d4f50c](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/4d4f50c34db62650c92c781455d84eec159624e1))
* **tracing:** add SQL client OpenTelemetry support ([15e773f](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/15e773fdbbb4c00676f92bd228967854ae20f770))
* **tracing:** propagate Activity context in sync pipeline ([48370a6](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/48370a6f457804d2a309d87fb7b307c4dfe77db6))
* **user-access:** add UserProjectAccess entity & table ([9c86bb1](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/9c86bb111ed29e919a6c66f933a52b8035941ade))
* **vault:** add modular Vault config provider ([9d2e930](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/9d2e93039e8c7bbede1a3a671300622154c19ac9))
* **waldur-collector:** async streaming & API updates ([bde463b](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/bde463bff7897e6e9e4c780f5d8418e262cd4622)), closes [#147](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/issues/147)
* **waldur-sync:** add data consumer helpers and dead-letter queue ([9e23c4d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/9e23c4d35346a3f9ca22a4d77a92d7a54679ab97))
* **waldur-sync:** add OpenTelemetry tracing & metrics ([76a2812](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/76a28121177a85b537cad429eace611fd2eeb180))
* **waldur:** add allocation unit mapping with conversion ([8b7cc9c](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/8b7cc9c58b983805a34af7df441843d30361f8fb))
* **waldur:** add channel sync for resource export ([46d7b4f](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/46d7b4fcd4700a975929b0b408c3bff3cb0aa910))
* **waldur:** add config flag to enable/disable integration ([fbfbb87](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/fbfbb87e8808daa0cd32992fc739aa77f85ebb8a))
* **waldur:** add EF Core persistence to orchestrator ([f960946](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/f960946ae2e878b4b2ac3e09f6761103db1b6409))
* **waldur:** add endpointapikey option ([9437ecc](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/9437ecc82054360ec4300d98b5d0f4cf7cb13606))
* **waldur:** add header config and update endpoints ([3533f30](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/3533f30479bb0de7a761778ccfaec3248938304f))
* **waldur:** add HeappeInstancesMap config support ([8869040](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/8869040c0368ca447a5e16deb8019197b70fbaa2))
* **waldur:** add IExporter host contract and export pipeline ([c4fb32d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c4fb32d2b4c9435ef0470e19591a2b6878049224))
* **waldur:** add offering validation and improve logging ([88c4ca7](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/88c4ca712aa43958bfd4c714376879a94ba65f45))
* **waldur:** add OpenTelemetry tracing, strict refs ([5c79b19](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/5c79b19eab2edb9ad472f7efc8d93e6c00e6f194))
* **waldur:** add paged streaming for API entities ([1b4fed1](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/1b4fed1e458cc2de7818910413896b02c7a2c669))
* **waldur:** add STOMP queue service and config ([145fe98](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/145fe9833375af80dbe1b2ae71b77054ce5c40b1))
* **waldur:** add system export user initialization ([4788c01](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/4788c0100a89bd72f43098d2ea4c359855396726))
* **waldur:** add Waldur integration module and contracts ([40ae92d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/40ae92d71663ddfd6e7f86e31abde3b3a83a8926))
* **waldur:** centralize constants and refactor export ([3fa1ca2](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/3fa1ca2cde8c475cb6a71504903d782abea287ac))
* **waldur:** implement lifecycle-managed PipelineOrchestrator with async iteration ([07b3123](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/07b31232240430a36702c68fb85637098b3d4273))
* **waldur:** implement nullable offering retrieval ([e86f548](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/e86f54827be8918fe5e952aac6cb2cc1148b7eb7))
* **waldur:** improve HEAPPE mapping and error handling ([ef7b340](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/ef7b340fccfbdaf7282b856d97ce8a48a7998872))
* **waldur:** make sync interval configurable via Aspire ([d72215e](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/d72215eeed4d7add90187b1234a06da4bfb801c4))

### **🚥 Tests**

* comment out skipped integration tests ([7c5d2a6](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/7c5d2a6aba23d382356c29353e656b4c75458e16))
* ensure exporter ignores invalid offering components ([7a181cd](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/7a181cd5ed97f68fbf5d2f5fa2ef82f4d5a7ae39))
* improve activity collection and validation logic ([eb06367](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/eb06367a87ce5a6d2f72d3a46026a0ae7821152e))
* modernize fakes and add WaldurExporter tests ([eb04a55](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/eb04a55ce4b82897d1d3ba77a70d7dc37c43d1bd))
* normalize SSH endpoints in test data and docs ([30f07c2](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/30f07c2e01116f6656648d7ae5c37f85f29a7651))
* refactor FullPipelineFlowTests setup and DLQ checks ([6cc97a8](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/6cc97a861bb6784fb5f76a83f6753e185b9bf523))
* unify provider external ID usage in test setup ([7886ea8](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/7886ea84095b02ba0206caaac2b091bc0c6bef29))
* **adapter:** add fakes and helpers for project creation ([1470258](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/1470258df6addb366da0ba3c996891efbeba0138))
* **e2e:** add Aspire-based endpoint tests project ([1bd515f](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/1bd515f0a8843c842153f7b2e478c0c542095f20))
* **integration:** skip tests requiring external services ([0375a19](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/0375a190f91b247562b7d09e17db4fbd4f1a491f))
* **waldur:** add background sync unit and integration tests ([1f807c2](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/1f807c2d0ede6358cb3260c77ef65068bdb45e4f))
* **waldur:** add fakes and null-guard checks for orchestrator ([b268b70](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/b268b700f4ff29d8fd950d60db4be9d0d15904eb))

### **🚦 CI/CD**

* **test:** streamline test execution and reporting ([256ceac](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/256ceac7d25003693a03d4007e9e5283b298ee6c))

### **🛠️ Fixes**

* slnx path typo ([81b17d5](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/81b17d5e0545047374c51bdd28a1348d20f32deb))
* use double underscore for config key separator ([46920c6](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/46920c6d19c811c0e5acb0ed595e2113049cd7e3))
* **core:** update provider ID handling and test seeding ([ebbec26](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/ebbec260dd8bb8311e1dfc45620fcdfd93fc7d93))
* **exporter:** trim hyphens and spaces from location names ([6dbf8e1](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/6dbf8e1de26f359d927cd2772600b0fa1d2b1ecc))
* **gateway:** remove duplicate response-transform block for Cyclops forwarder ([7087eb6](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/7087eb615584ec55397c1ba984233d589050102d))
* **health:** implement health checks for gateway and defaults ([46b10b4](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/46b10b48a1de8ac8914fb75b3d64f375eabe4864)), closes [#175](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/issues/175)
* **HEAppeVersion:** add parsing boundaries for extracting Version object from string ([d034665](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/d034665aaf911bb8d1eb7504c71c8c6d395d215f))
* **orchestrator:** set projectId from processed entity ([d0c41a0](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/d0c41a0747d22882581d06be2c30bab3f0c79088))
* **sync:** add error logging for DB save failures ([e7e36fa](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/e7e36fa9296b468a0414b8bca9dfb8d71a644b57))
* **tests:** correct FakeServiceScopeFactory implementation ([a7348fe](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/a7348fe9013aa36838def88856cdc54541f3b3a5))
* **validation:** correct null checks in data validators ([6306095](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/6306095d3079b19867378113e0a5815efede32ca))
* **validator:** improve resource BackendId validation ([8832f82](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/8832f829a5fa01f928c78b6c8de99d3a99453b00))
* **vault:** add AddressPrefix for flexible secret paths ([e88d600](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/e88d600314eed7af09ae90925b0d6e724850f17d))
* **vault:** centralize Vault path construction logic ([336f2c3](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/336f2c370dbda7b77f422c82299bc3192c487724))
* **vault:** log vault parse errors at error level ([5d215e7](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/5d215e76639387e70800add1ca1e657ef580fa1e))
* **vault:** recreate secret_id and role_id files cleanly ([ea34d1a](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/ea34d1af9e3cdda73820c7c36125c0c6630ecfe4))
* **waldur:** improve config parsing for enabled flag ([86378ea](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/86378ea1595ffcfb0dc10d6013586d0c150ffed6))
* **waldur:** reduce BackgroundPeriodicSync trace volume ([7110ad7](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/7110ad7aa93ef7673296e41a6b851c56632e7781))
* **waldur:** validate aggregate data before export ([c009d95](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c009d95fa78a419d735a89254c985a8b780ef8cb))

## [5.1.0](https://opencode.it4i.eu/lexis-platform/backend/userorg/compare/v5.0.2...v5.1.0) (2026-03-06)

### **🚀 Features**

* **cors:** simplify CORS policy and add logging config ([5bb1ea5](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/5bb1ea5c9bb814a0e8b346c07495b5d0561728cb))

## [5.0.2](https://opencode.it4i.eu/lexis-platform/backend/userorg/compare/v5.0.1...v5.0.2) (2026-03-04)

### **🛠️ Fixes**

* **api:** simplify update flow in assignment endpoint ([c108201](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c108201c6b7f55b56c2ff7274e8690434f005303))

## [5.0.1](https://opencode.it4i.eu/lexis-platform/backend/userorg/compare/v5.0.0...v5.0.1) (2026-03-01)

### **🛠️ Fixes**

* **auth:** improve claim handling for provider admins ([5e1b6fa](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/5e1b6fa42b8b6aa5be9936a8cae28a85366033aa))

## [5.0.0](https://opencode.it4i.eu/lexis-platform/backend/userorg/compare/v4.0.4...v5.0.0) (2026-03-01)

### ⚠ BREAKING CHANGES

* **seed-data:** remove roles and permissions sections
* 
* introduce BaseHeappeConnectionInfo abstraction
* **auth:** 
* remove role-based permission tables and links
* **gateway:** - /role-management endpoint now requires valid AccessToken for initial access
- Existing deployments must configure ROLEMANAGEMENT__ACCESSTOKEN or add to appsettings.json
* **gateway:** Gateway now resolves permissions from database instead of relying solely on JWT claims. This ensures authorization checks use authoritative role/permission mappings rather than potentially stale token claims.
* 
* 
* **command-permissions:** 

### **⏩ Performance**

* added missing indexes ([49ccbbd](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/49ccbbd7d2132bd7adf1aa6e5c02705e57a41c34))
* replace sync methods by async ([b5e73a7](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/b5e73a7585e50a03d1aa5da6dcc06dc584b264db))

### **👷 Build**

* **test:** update to .NET 10 and improve Xunit usage ([e61befb](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/e61befb844e5f3007b6e2349556ac7fb716c0d30))

### **👾 Dependencies**

* update Aspire packages and SDK to 13.1.1 ([725dace](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/725dace53fcc3b7908949676325a20a7f8114b5a))
* update dotnet-ef and remove xunit package ([8abbfa0](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/8abbfa0152f572d206232d583bb54a7bcbef81e3))
* update EF Core packages and clean up project refs ([9d56eba](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/9d56eba5b5292346e140fd6f50b62190c5dcac69))
* update OpenTelemetry config and dependencies ([3bede87](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/3bede870e01f39bdced1d7091251732cc946bc60))

### **📚 Docs**

* add Aspire style guide and update xUnit skill ([81b2cd4](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/81b2cd48f58d3d556ebf0f50bb79d99911b2fc0e))
* add docs index link and record decision about docs/index.md ([28c7a6c](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/28c7a6c93150e17d8e94581480849b1de1c103be))
* add linus decision note and history entry ([5030d71](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/5030d715a7c61aafd8d3e324f840b0e7fe75360f))
* add semantic-release commit guidelines ([89ec52e](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/89ec52ed5448cdc67f130747c1d4c75e17f19b41))
* consolidate decisions and add scribe log ([247689d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/247689d669ceeb6f26f139a90d1796fee5576037))
* reorganize documentation into docs/ ([73a6b37](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/73a6b37b05c346c170c331c50a802b9b2b6ed78b))
* update flowchart for auth flow clarity ([332fddb](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/332fddb116586bec37e65007b588301566bcb848))
* update README links ([c404675](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c404675b65ff74d536cf845b409971b7392b838d))
* update README to remove legacy docker-compose ([a95e53b](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/a95e53b34ff06ee7d36ba82217abcefb4aa2a1aa))
* update README with docs index ([27caae4](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/27caae43536eb213474e9b7ce72b0e3debbf27aa))
* **ai-team:** merge decisions from inbox ([fe82e8e](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/fe82e8e7fffa3a9d1f3bfd12282b3bbfd36999f1))
* **ai:** add decision inbox entry for gateway trace change ([1bd3620](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/1bd36203435ee78535188ee43985112a99a5b091))
* **architecture:** add initial ADRs and mermaid diagrams ([edb07bd](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/edb07bd72456953f5d05eab5a2ec868ca62c65a8))
* **core:** finalize feature slice docs, remove vault cfg ([e21f971](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/e21f971a1f933cd1c41be20e9c9c2440c20a42ed))
* **README:** update Installation to use Aspire ([e26e2a1](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/e26e2a14b9d42274dd0e03095559d26be28fd630))
* **refactoring:** add detailed refactoring plan for Core ([a93e241](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/a93e241438215e5076c083305975df95a7820445))
* **skills:** correct xUnit testing skill file path ([83a263f](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/83a263fd974d8789cc03c540bd3a9ccbaf6cfcf8))

### **🔨 Refactor**

* add global soft-delete filter to DbContext ([ddc582d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/ddc582d1ca5e4cf6d667e8f0b744f3ba07185b76)), closes [#72](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/issues/72)
* remove IHeappeVersionResolver dependency ([8876451](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/8876451e9d53061baf52b5fb9fe643082abc75a3))
* remove unique constraint from LocationPartition ([ba1d53c](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/ba1d53c02b538c4a8aaa9d4392e6232135fc9795))
* simplify node type filtering logic ([6c41c72](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/6c41c72e6936b5aa0588150f29224f20ad68f604))
* introduce BaseHeappeConnectionInfo abstraction ([6263354](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/6263354cbe4886d4f334fc9cde4e8453b68fa256))
* remove ProjectCommandTemplate feature ([0a5dae0](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/0a5dae058f431b846f8a7deaa039ca001a0a59af)), closes [#72](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/issues/72)
* remove ProjectCommandTemplate table ([179b5b6](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/179b5b69ca04140b9da909e6cae2f54193ba746b))
* **api:** improve DataStagingLocations endpoint code ([df3b476](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/df3b476c0655a0ebfc2f8fa29e085bf443dd9fa9))
* **api:** simplify import/export endpoint URLs ([a8d44e1](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/a8d44e19a36c0ad313f06d6d63435297b5f8d73d))
* **api:** update container config and cleanup legacy code ([1efdfe9](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/1efdfe9b411b227158c9047520c1346a37240505))
* **api:** use shared auth helpers and improve provider/user id extraction ([82c780f](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/82c780ff1c88cd86807a1606a94f6924cfa6d898))
* **apphost:** centralize parameter management ([7c3b379](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/7c3b3798ab4d84e118381c8d59bfe206387d7644))
* **aspire:** update S2S token, remove immudb, fix URLs ([13a1365](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/13a136582b8923cf79d2ec6c61fbb94b6e604422))
* **auth, import-export:** modernize core features ([f066aed](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/f066aed7c959bbf52b945c3e5c22e346724c067d)), closes [#72](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/issues/72)
* **auth:** migrate to claims-based auth and config ([3b2d799](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/3b2d7999cd7dfcc66093fa81b9e83d59f0b4c954))
* **auth:** move auth extensions to UserOrg.Shared ([e5e44d2](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/e5e44d2093111dd9bd0b1e41c2d0a3aadb15d3a0))
* **auth:** unify user/provider extraction from claims ([8b6d011](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/8b6d01193d26f766f573187aefc35ef9af5ac6f9))
* **command-permissions:** overhaul allocation model ([9580066](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/9580066b59a2d01f3589ccf19c6c1c979a3c05e5)), closes [#72](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/issues/72)
* **command-template:** modularize and improve API ([44f336f](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/44f336f9a2a5b7599770c3492795671cd1f26230))
* **connection:** extract command template pruning logic ([7eee6e6](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/7eee6e6bd3c0fb4aa301766d13a03db4b23f4a7f))
* **core:** improve code style and tracing logic ([c05d8b3](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c05d8b33e6a38faf8b4e8bc33aef8075dbdf9805))
* **core:** improve safety, tests, and code clarity ([f06dbae](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/f06dbaee61a5d8665b997ccb012857ba69b31197))
* **core:** improve user role caching and mapping ([5d3172d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/5d3172d4391aa256c404b6c0aab2c330ccfdb0cd))
* **core:** improve user role caching and mapping ([783ccfd](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/783ccfd08273a1b3b0a92269c6398cc8027de3c8))
* **dev-env:** disable Keycloak, clean env var usage ([75bf172](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/75bf172bab4941713f406a7fd6758a258914a0c8))
* **efcore:** improve migration and context handling ([6854e44](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/6854e44517b21e07660e09e7d80932e99466674a))
* **emailsender:** improve integration and docs ([cc98dae](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/cc98daec91e01a63e781a184092d111792b9041c))
* **endpoint:** update model binding for migration API ([b1456a1](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/b1456a19eb9c83b451e5f7205b135f4e2bffdcd6))
* **errors:** centralize and improve error handling ([afa8fee](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/afa8fee34c629edb9e5f7e84515872c7191c74ce))
* **errors:** standardize error formatting and cleanup ([27f5149](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/27f5149f48fedd243d62fe270552094cb6a1e54e))
* **errors:** use positional formatting in messages ([18d4e67](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/18d4e6767036e774d326065ce69d8763fa1c5901))
* **gateway:** extract tracing transforms into AddStandardTracing extension\n\nConsolidate trace header and propagation logic into a single extension method to reduce duplication\n\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com> ([3708994](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/37089942bf619b0eb189bad3c4d28deab9887a34))
* **gateway:** use UserOrgRolesService for permission resolution in authorization ([5d04169](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/5d041691a14617894ccd60d38077bf8b6c103d05))
* **heappe:** adapt CommandTemplateMigration endpoint to accept dryRun ([8ab9d49](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/8ab9d49c9a39d572d3c9d606d6e7c8aa72685dd5))
* **heappe:** modernize integration and permissions ([c061dc2](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c061dc211b319274958b0a8c987f551c7597f3e3))
* **infrastructure:** improve migration idempotency and clean UserRepository ([950378e](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/950378e3aa29f6cd8c2c9aea9a2baef90545fe3e))
* **migrations:** improve error handling and logging ([6876143](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/68761433b18fd6d3e861ad49d7561ad073f07313))
* **models:** remove hardcoded readonly ID logic ([69ed8aa](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/69ed8aa1f01240759b93f0b4c4325c2ce8718471))
* **options:** update DI config binding approach ([f89b3af](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/f89b3af4aedc047f5e419709a4bc0d919d707d2f))
* **persistence:** unify and update EF migrations ([87f603f](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/87f603ff1497a89475b7de9d8f20d29d623ab527))
* **program:** modernize and clean up Program.cs ([2e17067](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/2e170672f7215945bd0dcffb48c7ef82bdc02c76))
* **program:** simplify URL annotation and error handling ([435fd22](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/435fd22cfae2b10127b9bc6f226b5c31ff8a02e5))
* **program:** use var for local variable declarations ([6a4bd57](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/6a4bd5747ea3e1e1a0bf4f4f1199d7de989d3344))
* **rbac:** reorder ProviderAdmin role in JSON ([dd8b881](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/dd8b8814b0ae842491f9baf19f43279b8e8a5c30))
* **release_stage:** remove unnecessary before_script commands ([50ee2e6](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/50ee2e6c4b824c057c0506561705d188b5c586a1))
* **release_stage:** streamline release job configuration ([64ea529](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/64ea52992688fb42659e569c6f28c9f83f32fa44))
* **role:** unify role handling and update interfaces ([a9976ef](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/a9976efb3c29316bc053544b0cd24be8ae31c737))
* **role:** update role creation and sync logic ([3915fe4](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/3915fe4f86b12e076b0836fff8e888eec9810c18))
* **seed-data:** remove roles and permissions sections ([62c506e](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/62c506e729a3b6d9c8a25d5fc9a2ce61b3c23c77))
* **seed:** remove system role permission updates ([2bc41a1](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/2bc41a1d96260203286969c86e043a21d081137d))
* **shared:** centralize role constants in UserOrg.Shared ([59e3ba0](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/59e3ba000de8666cbb8ed82d64ecec9e93e8a4ab))
* **shared:** move AuthConstants and EServiceType to UserOrg.Shared ([2b89738](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/2b897387566b47956c7e084eb9667feddee28cfc))
* **sync:** improve partition sync and logging ([505b880](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/505b8804e49a38d876bfeaa5ab8c0eeadd2ea319)), closes [#116](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/issues/116)
* **tests:** remove global Xunit using, update scopes ([4a4682b](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/4a4682bcec07fcfcd74aab1641717eba3a61144c))
* **tracing:** consolidate response trace header logic into shared TracingHelper and use in Gateway and Otel middleware\n\nMove duplicated response header setting to UserOrg.Shared.Diagnostics.TracingHelper and call from TracingExtensions and OtelTraceResponseMiddleware\n\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com> ([a50db57](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/a50db571ab7ce76111a37c9188a5f83f25a0aef8))
* **user:** inline SetProperties in UpdateUser ([83ed580](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/83ed58033f033c659bcf447976a6b2abc324f75e))
* **userorg:** migrate to RoleInfo and role service ([664182e](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/664182e7827dde83ebceac6146c74958a0c25ba6))
* **userorg:** migrate to RoleInfo and role service ([9d956c7](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/9d956c7f9915bdbc4a20840b66b84d806a4a15f0))
* **vault:** return DataStagingLocationKey for DDI ([1d9cc57](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/1d9cc57d269133ecf4f2149a6632386b867d4339))
* **vault:** use Result/Error for error handling ([8efdb01](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/8efdb01f72713d1cf59fbf1150608ac88de63ed3))

### **🧹 Chores**

* clean up ([39459ce](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/39459cef1854342340557c6927cdf546e3d67eac))
* clean up ([28e205c](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/28e205c3be16d729ec04eb3ded94786f6784aee0))
* dotnet format ([6d1a6c4](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/6d1a6c45420d2cdbbe67ff343f78df358cb79516))
* dotnet format ([5920844](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/5920844f38665e837af183be5c63f174248f3c7c))
* Format ([525bae6](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/525bae6f2dd5bc4321fd80f1da5b859659ff29fc))
* format code ([0a0463d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/0a0463db1b55a518989bfa72e852cbe3abe4ae16))
* remove Forge.Core.EndpointAuth implementation ([070f0b1](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/070f0b137ae6a6166eee84f54bcd2a5ad0b97fba))
* remove Inspect project and related code ([f1d1402](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/f1d14024bc04e4a6b5aeade8a6fbe3688256e821))
* remove obsolete Forge.Core.EndpointAuth project and update HTTP tests ([d158de8](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/d158de88ba934550ae34e35a388f1cb91a133db1))
* **ActionLink:** add default value = 120h ([0e9d89c](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/0e9d89c532e7e191379517bcace5427796133bee))
* **agents:** add .NET/TDD agents, docs, and standards ([aa5aaf2](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/aa5aaf2bcd6afd88a4530d2aa4fb03342dcaab9b))
* **agents:** remove duplicate AGENTS.md under .github/instructions ([5e46058](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/5e46058a025f99b00608e623e88437b78ebf875a))
* **api:** add GetValidRolesForProject endpoint ([5ba1a11](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/5ba1a118999ca0af2025fc9066a76108632ded32))
* **api:** rename POST /Update to DataStagingLocations (resource-based route)\n\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com> ([e3b904c](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/e3b904c2fde7a2d71155cb605ca1167d4507eb7d))
* **ci:** apply run rules from old_stages to stages (rules/when only) ([567786e](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/567786ec2abdfd5567a6a593e0188530e683147b))
* **context:** add code-quality and test-coverage standards ([2f53df1](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/2f53df134abe7e61fa6a8f419c2a20e72cfc4ea8))
* **docker:** add ping and telnet utilities to base iamge [skip] ([dd8b5a5](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/dd8b5a5cf48cc8a038362cff13fa4faeac3eac1b))
* **docs:** apply formatting to docs and code ([138b92a](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/138b92a9bfcbc3610f812f5e5c5338c42b0c1d94))
* **editorconfig:** update var usage preferences to suggest ([b1b232d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/b1b232ddc725f2e500cf89dc9d88edca9d13cd5c))
* **gitignore:** add /NUL to ignore root-level NUL files ([74a7eda](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/74a7eda09d022cebecf28fefcf72cd2f4eb3ea69))
* **gitignore:** ignore keycloakdb.trace.db file ([0a92425](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/0a92425e9a4b9bcb82b8fb412e45b08e18fd71cb))
* **http:** add UserOrg API request examples ([97be6e0](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/97be6e0b66dd07531064cd8b3069bf72c5667cca))
* **http:** update bearer token in http file ([5f1961d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/5f1961dde39da604ece12e5039b50da980a3035c))
* **import-export:** add logging source-generated helpers and replace import/export and permission logs ([e80f7f3](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/e80f7f3e7989c16c558ad2f39173af8576ddf3f6))
* **logging:** continue converting src/Modules log calls to LoggerMessage extension methods (Role/CreateRole, EndpointMap) ([70045b2](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/70045b2505335a53ff1676de44508429e0a15733))
* **logging:** convert EndpointMap and remaining src/Modules Forge.Core logging sites to LoggerMessage extension methods ([c4da773](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c4da773b807c9eb87a676a08d3df836eb4017af9))
* **logging:** convert more CA1848 sites to LoggerMessage (Role, Permission endpoints/services) ([7a571d4](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/7a571d4acd4984dc8142f088b8af4086711d0aa0))
* **logging:** convert remaining Forge.Core startup migration logs and endpoint/role/permission logs to LoggerMessage ([63cd269](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/63cd269b0290f7456d0eef1ce65b5c6a71d52b74))
* **logging:** make LoggerMessage methods extension-style and continue converting src/Modules logging sites ([9875da2](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/9875da2d883de9b4b3cb8d70862ae5affe9241fa))
* **rbac:** update endpoints and permissions mapping ([5a92d64](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/5a92d64643a2062b1a23959c5d8c600cb841ae42))
* **repo:** add migrations, tests and miscellaneous gateway/apphost updates ([7f5ea8f](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/7f5ea8fac8b5906015382705a83fd13734ca669c))
* **role-management:** remove redundant PathPrefix comments ([da15ac6](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/da15ac600dfce4a73185052153218a134ab58499))
* **seed:** remove roles and permissions from seed data ([c5eb0c5](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c5eb0c56f4ac62821d80c9eb09f1c4809fe17fbe))
* **skills:** add Excalidraw diagram generation system ([2732912](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/2732912a0da9aaeccf3a7f8f530192c0a429d9e7))
* **skills:** add xUnit testing guidance and best practices ([343d265](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/343d265aa0a104d5a4132fc3f3068aed1a230a08))
* **slnx:** fix casing in HashiCorpVault.Tests project path ([fc2f4b5](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/fc2f4b5a2f62b501306fab19e1e75994b2326e58))
* **solution:** reorder project references in slnx file ([52c31ca](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/52c31caf9b32934352d4a282d0f7eb6479a77605))
* **solution:** reorganize test project folder structure ([c9bfa35](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c9bfa35d267b2998bbdc8ed47e6caf05513ecca0))
* **solution:** update HashiCorpVault.Tests project path ([82bc104](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/82bc1048cf1c30a9275c25fc6572d94bbe3801a3))
* **tests:** include new HashiCorpVault tests in solution and adjust Heappe unit tests project ([98744f8](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/98744f88781cb3e04c422c57b8f4bbc403c68777))
* **tests:** include new HashiCorpVault tests in solution and adjust Heappe unit tests project ([556a466](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/556a46660e30e04bbdc589d7585814c1af6c05ab))
* **tests:** migrate test projects to xUnit v3 ([63d765e](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/63d765e926b69c836278ca33eeadb8055cbd0595))
* **tests:** remove Infrastructure.Tests project and files ([af1ce91](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/af1ce9139ed79bce9d11d8b751d60b5587e90b1c))

### **🚀 Features**

*  Finished method CommandTemplatePermissions ([11e70b7](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/11e70b7ae6f84d18173ed4be4a95afe2eb4f29f4))
* add HEAppE sync on project resource assignment ([918dc9e](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/918dc9eaa3b983fe56978e1862d90f2fad5cd9bc))
* filter clusters by resourceName ([3c8b257](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/3c8b257903b0928ddd001231f426516fb7d12edb))
* increase ProjectCommandTemplate name length to 200 ([59b4c26](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/59b4c262bf57103f170d48e2ab72bd307d223c68))
* prefer configured issuer in OIDC endpoint ([85b734d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/85b734dd57281d064fc9c94a75d4926dabfe3271))
* support template preparation scripts in migration ([c7c5396](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c7c5396b62d4bb1909df733636134ffb7f6d1d2d))
* support template preparation scripts in migration ([d24179b](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/d24179b8d54288b77dda1ef7208eb04bd9e8f3bf))
* update unique index definition ([e1073ff](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/e1073ffbc80f2f16a7aa1a8f9b0fe79293b382ff))
* remove role-based permission tables and links ([3654dc8](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/3654dc806e7bd138d9b8e1af4f1d4001e3a1e342))
* **adapter:** integrate HashiCorp Vault credentials ([5a86fbf](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/5a86fbfefeee95176d1d5bf0dd229064da472706))
* **api,infra:** update emailsender and request loading ([01efdcb](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/01efdcb23c613b12f0b855786a2b60c636585cd6))
* **api:** accept both object-mapped and string JSON payloads for DDI DataStaging update ([9e2b85d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/9e2b85d7dca271a46541a5c450f5addf1e451612))
* **api:** accept both object-mapped and string JSON payloads for DDI DataStaging update ([2213a70](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/2213a708b96987ab5f36c8ce3bcb7a90c6028e1e))
* **api:** add HybridCache for user info endpoints ([cf2182f](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/cf2182fb8b6929431ec3bc05c40bd3b853f44de7))
* **api:** auto-create user on first login via token ([7149189](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/71491893d7d8370d6a4d4e2564f026b656f3aee7))
* **api:** change role param to roleId in assignment method ([d398a31](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/d398a31e2112f73c3e0bbb5ffa6bee29442cda26))
* **api:** improve authentication claim resolution and require authorization ([ada297a](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/ada297a4324b458b4370e557fbd7cde41fcc55c3))
* **api:** improve validation docs and error severity ([98017e7](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/98017e7764388bbf305aabe34ef3425b97627487))
* **api:** prevent duplicate project resource names ([6836178](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/68361784b4b801708334c205488b4cbcb5a7ff2b))
* **api:** refactor project role assignment by name ([a170703](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/a1707034fc83a36185f3d468ed57a0538f5bd9ec))
* **api:** remove admin/provider access restriction ([e929243](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/e92924336203f44745f2cb35f683e9f7c2767057))
* **apphost:** add SQL DB restore via dashboard action ([464b19a](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/464b19a1bc7d025f81005a988583eb01d76f5f32))
* **auth-discovery:** add JWT/JWKS, certificate S2S auth, and gateway discovery ([6658f49](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/6658f494f1799080a5502d417edb991e34a5dd60))
* **auth:** add email sender HttpClient registration ([3390fc2](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/3390fc218e060bcf5e9f2fe7948f5cd907cc0372))
* **auth:** Add Gateway JWT validation & tracing ([68ac792](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/68ac7926d99f8c303c02cb151394b80e2a55e9cd))
* **auth:** add method to get user project roles ([bdcc1ea](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/bdcc1eaa5c3d49c05e81428ba583c14575359803))
* **auth:** add public endpoint scheme with S2S token ([ccc39bb](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/ccc39bb051ece496fd8c1da0ae8013109f93d776))
* **auth:** add support for X-Original-Authorization ([3289168](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/3289168f2d1daf9d03b721a0a809eb4f1d4b9982))
* **auth:** enforce auth on Heappe API endpoints ([db29d86](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/db29d8626e57be3d013ef8e8f9dd98a22d899643))
* **auth:** handle "Admin" role in permission grouping ([bbef688](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/bbef688ff0fe8abab2aa90555dce4d64fbbb4fab))
* **auth:** improve role claim assignment logic ([09abe13](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/09abe13b35c71f22c6b9322bdc4cb797f4dac31f))
* **auth:** improve selector logic and gateway URLs ([d4e509a](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/d4e509a0c117319542946c81b9f78220324b7447))
* **auth:** integrate Vault for API key authentication ([32f709e](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/32f709e505ecded940028acf023152d87ee6277b))
* **auth:** require explicit service perm for endpoints ([dcb4206](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/dcb4206cd452b64219e7285b62869ad8b2b647a9))
* **auth/rbac:** improve OpenID, roles, and migrations ([151087d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/151087d35ecf6c960a5ee3a2d5d0ad4e62e868dd))
* **authorization:** enhance vault and keycloak configuration ([b1e2f85](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/b1e2f85103f4eebf461addfea6961d62f2f8b28e))
* **ci:** add build and push for Gateway image ([8db472b](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/8db472bbb7b0ee2bd889bfd09e3adc8594d4e2a0))
* **ci:** add ContainerImageTag property for versioning ([5098c18](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/5098c18e581a0b8548cd241318e770d732b31660))
* **ci:** add dotnet restore command to nuget_outdated stage ([d953cab](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/d953cabae19e26eb30b68c4c1f678fd97d2ccb31))
* **ci:** add Gateway image build and push step ([981bb69](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/981bb6904a0f61a1fec5ad764d9148700d9d4e38))
* **ci:** enable test stage in CI pipeline ([0043bbf](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/0043bbfa22586491854233dac356a5c7da670fb3))
* **ci:** enhance build and test stages for migrations ([65a0c4d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/65a0c4d55f588493bd209f821297e0aaf2576899))
* **ci:** enhance CI configuration for .NET builds ([c8ca22d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c8ca22d283eae4430bfa703b7d0c226fbebb4b66))
* **ci:** enhance Docker build and test stages with JUnit logging ([ff15b04](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/ff15b040a77b75ee48327d6d7e6218fd34136b40))
* **ci:** enhance docker build process with buildx ([87ae010](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/87ae01026a9cd2ae494884f91617db963ce00c37))
* **ci:** simplify docker image build and push process ([1abe7f6](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/1abe7f65e028bc9ca6f6e142ef70f1c3a98773cf))
* **ci:** simplify rules for CI jobs to trigger on push ([4a11410](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/4a114108f89832bb7f96271eab5830499c4990dc))
* **ci:** streamline Gateway image publishing process ([d3eadce](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/d3eadce99d4f4553114cf55ad319471feb3c2ad4))
* **ci:** switch to docker buildx for image builds ([e8b9201](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/e8b92016a0ab88e9d5c34570c6b8b3de39338f28))
* **ci:** update build rules for manual execution ([a7fac2e](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/a7fac2e5717407bde2bba3b4a7fb21b389b3e7c1))
* **ci:** update Docker build process to tag images correctly ([2fff3d3](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/2fff3d35b675afcc3bf2a0e0839b0e2038331c34))
* **ci:** update dotnet format job to include restore step ([cfb5ebd](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/cfb5ebd70fdd4f4bafe24e792c9b30da4d2f79b8))
* **ci:** update Gateway image publishing process ([7f60177](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/7f60177a694fabfee44d04b87acbb59b1bb4a853))
* **ci:** update secret detection and nuget rules to manual ([fb5f549](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/fb5f549a6666abd4284120361100aca6c2eebed5))
* **command-template:** add per-resource template support ([74155c2](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/74155c2a5cb842383b6fb8d07ca7c3bed31f53af)), closes [#72](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/issues/72)
* **command-template:** add support for generic templates ([f7104dc](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/f7104dc43da1c9f9ae3b26da736eb93bf25d9df5))
* **core-api:** accept object-shaped JSON for DataStaging /Update ([6ea6d1d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/6ea6d1d28a10a03326cd05a067c37b3ab0d6028f))
* **core-api:** accept object-shaped JSON for DataStaging /Update ([a39b6a1](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/a39b6a12a8b7e28b79797e10056ca5f9572ab183))
* **core:** add CRUD operations for endpoint permission mappings ([89b7267](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/89b7267f70e5ecd7996f56520abc703e9e573f1c))
* **core:** add permission assignment validation ([1631a91](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/1631a919f00ece5327f2e656fef04fa5277192c5))
* **core:** enhance role management with description updates and safe deletion ([7842d06](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/7842d063b609e59ca305b1c055b7c86b2da090ce))
* **db:** add Type enum to CommandTemplateDefinition ([c4fa953](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c4fa9532733f7a0a9a45a17138d8405479e92899))
* **db:** improve SQL restore UX and error handling ([ce52dc9](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/ce52dc9a8c6f17d7a473e9e29d6846a301348bda))
* **docs:** add AGENTS.md for coding agent guidance ([89ceed3](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/89ceed3eca2787922eff1a6b4250ffb4bf4e4be0))
* **docs+api:** add docs index, agent skills, API fixes ([746dbf7](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/746dbf72a2a5e26058b61a4c2fd56c067c5ea226))
* **domain:** add status to command templates, new URL field ([73a5114](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/73a5114a75d27f7dbdf138c5ebad41563c76fbbe))
* **endpoint-permissions:** use HybridCache for snapshot with stampede protection ([678af39](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/678af395f7118a71ad0c1a21f31d57db881b5e18))
* **gateway:** add auth method tracking and early 401 for unauthenticated users ([37c4a09](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/37c4a096c17a454d5c073cb1017d08975bcac79f))
* **gateway:** add centralized state management for RoleManagement feature ([26f2409](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/26f2409c2eb1b2751db2392ac324a1bcf190c8e7))
* **gateway:** add comprehensive styling system for RoleManagement UI ([3992351](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/399235155bb01589183085dcfd9be00975afa095)), closes [#0f766e](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/issues/0f766e) [#0ea5e9](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/issues/0ea5e9) [#10b981](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/issues/10b981) [#f59e0b](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/issues/f59e0b) [#ef4444](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/issues/ef4444)
* **gateway:** add endpoint list and mapper components ([62fc7c3](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/62fc7c3202bfcc5bf9734b7c2a91deba3e90f004))
* **gateway:** add OAuth2 token introspection support ([53d1c6f](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/53d1c6fa33fd2ac463d1d8d0d91c961960c98615))
* **gateway:** add permission list and editor components ([569c696](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/569c69609ce210c751c6f7343be3406f1a840b7d))
* **gateway:** add reusable UI components for RoleManagement ([37edd66](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/37edd66653cb2f7c6c83c325d1a3bc17476d1680))
* **gateway:** add role list and editor components ([e1fe04f](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/e1fe04fcc8d2ffc29c981a16446c3e2157f4e988))
* **gateway:** add secure token-based authentication for RoleManagement endpoint ([adff21a](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/adff21adaf315a411927a46f8740ac6ebf75f5e4))
* **gateway:** add W3C trace propagation and cleanup ([934a2e1](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/934a2e132e890c4fbc719f1680c55980c6223d7e))
* **gateway:** allow pass-through for mapped endpoints with no permissions ([e10d8ba](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/e10d8ba167bc6a15520e2f9f5f4a6ca285962a9b))
* **gateway:** enrich traces with route template and concrete path ([384e1a1](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/384e1a10c46f6ad7ae20223c5a31ffa7e6b780c5))
* **gateway:** extend RoleManagementService with endpoint and permission CRUD ([d4442a9](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/d4442a9da1493824409fbe8542e8397b26e592b0))
* **gateway:** implement main RoleManagement page with tabbed navigation ([76ef881](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/76ef8814ba136f0d07150aca3175e6a1032be750))
* **gateway:** register RoleManagement feature services and add documentation ([5d2f92c](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/5d2f92cfe3813873f133f07a26ef12b956f0aa23))
* **hashicorp-vault:** add DataStagingLocationKey model and bulk update result ([1b38849](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/1b38849b521c97de4c86fcb21c236cdfc5f2b5e4))
* **hashicorp-vault:** add DataStagingLocationKey model and bulk update result ([d735e96](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/d735e961c33ae0df4b12fe09f5202589541df1b3))
* **hashicorp-vault:** add HashiCorpVault client, options & DI extensions ([ad89aba](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/ad89abab8ddf1b204209397756c0bc79bb299330))
* **hashicorp-vault:** add Minimal API endpoint for DDI bulk update ([980b58d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/980b58ddf2e434527eef4da1b2033a2ca9503972))
* **hashicorp-vault:** add Minimal API endpoint for DDI bulk update ([0c56bcb](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/0c56bcb8ad86f1ec3052393945563f79047b4b76))
* **hashicorp-vault:** add unit tests for bulk update behavior ([b900d5b](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/b900d5b96e1e0247044b34ee798307e5d912caa4))
* **hashicorp-vault:** add unit tests for bulk update behavior ([af79a31](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/af79a31cd668e3df0b80dca0ec599d13c757bfe3))
* **hashicorp-vault:** add UpdateServiceAccessKeysBulkAsync to client interface ([b9cd879](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/b9cd8796857844f981697b2a8c03bcfb952d7f9f))
* **hashicorp-vault:** add UpdateServiceAccessKeysBulkAsync to client interface ([98f9c33](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/98f9c33b1aa34d0a6e59aa2477b3859fa9ba95a9))
* **hashicorp-vault:** implement bulk sync logic in Vault client ([0b161c1](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/0b161c12a6ea3fd4a3001933e8299fe2e90dfe92))
* **hashicorp-vault:** implement bulk sync logic in Vault client ([3271ec3](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/3271ec3be4145c7921893d320b741444ea3e9d06))
* **heappe-db:** enable EF Core migrations support ([d34da29](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/d34da29485f28c110aa903c5f67ae4e1f6bf0a8b)), closes [#72](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/issues/72)
* **heappe-sync:** add background sync for assignments ([e7eb3ea](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/e7eb3eabacbd057e8d8ef80bf83254bdf6d79c71)), closes [#72](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/issues/72)
* **heappe-sync:** add background sync infrastructure ([c199710](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c199710b8c8387fe12369e1c53667430074a62e0))
* **heappe:** add dry-run mode for command template migration ([6d9552a](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/6d9552a3ebecc41efa36230834ebfa26bea74f9c))
* **heappe:** add endpoint version retrieval methods ([91123f4](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/91123f46bb43e19cb7c7acf8a96e5ff1b4137cc0))
* **heappe:** add ForceRefresh param to cluster request ([b30ff3f](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/b30ff3f43ad1f7241bc0d5c66c1f463736648f92))
* **heappe:** add GetClusters method for cluster listing ([cb689d5](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/cb689d52cc056350e27d5d91f0d7d233adf5285e))
* **heappe:** filter clusters by active command templates ([5ddb2ff](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/5ddb2ff34893133bb896e2e66241cfcb314453d2))
* **heappe:** POC add command template permissions endpoint ([66cf3e4](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/66cf3e434871cde96af02c26ad3b0503092c65e1))
* **http:** add global User-Agent handler for HttpClient ([2d4c473](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/2d4c47390f0dd17ec7558a95583278a9cea4e846))
* **import-export:** add generic typed import interface and source-gen support; migrate Permission feature to typed import ([cb7a8cd](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/cb7a8cd46ddb39708443b837ff7ae75a04b0dbd2))
* **init:** add ProjectCommandTemplateInitService ([0c75764](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/0c7576419b22c6cdcafe0f427440664fd1bfb970))
* **libraries:** add Forge.Core.EndpointAuth project ([d8eaf24](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/d8eaf24a42c6186f0bc9d9a517dec08185917e9e))
* **migration:** add command template migration endpoint ([ec14bdc](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/ec14bdcb9373dbf37f6cb1650e8c331ed033db25))
* **migration:** add project resource fetch for migration ([c488c70](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c488c705493b7af97d5c89f593e5ac6f202b0cd7))
* **migration:** return migration summary in response ([19eb8be](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/19eb8bef0b43520d3e1e84ea024ec12abfdf895a))
* **migration:** support preparation scripts and refactor ([6ee4c09](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/6ee4c09190b4b346a5d9c689421c1abe51e14509))
* **migrations:** add centralized migration CLI tool ([aaa96bf](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/aaa96bff316d0aa7cd0bb579f3e8e602fc0bf9b9))
* **model:** default empty descriptions to "placeholder" ([5176473](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/5176473e8da0efc1dfa26cb64c9ce0a472e7c11f))
* **model:** set default for Description property ([bdd48b7](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/bdd48b728bc1149a46eccf00aec47140ffdc7dff))
* **models:** add EOperationMode to core models ([926540f](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/926540f2b77fbf286a888434e119e09b0103f818)), closes [#127](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/issues/127)
* **permissions:** add mapping CSV and docs ([cd25138](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/cd25138f89d46ebf37f2f2f2db5a4e44c23fb28e))
* **processor:** log start of work item processing ([cc2af23](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/cc2af2347448ff682251a6028521985c95db9e87))
* **project,role:** refactor role retrieval and comparison ([97f29b1](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/97f29b1ef06f2d69dfd309536815e20007d18fd9))
* **project,role:** refactor role retrieval and comparison ([039d942](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/039d942e19a2c851022446ae58d343ca807601dc))
* **project:** invalidate user cache on project changes ([fbb9222](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/fbb9222207c20ad3c5080868db1956de31b700b0))
* **proxy:** add optional dashboard forwarding support ([e9acd2e](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/e9acd2ea6e51798fb69d8cb624704532b41861c3))
* **proxy:** rewrite Location headers for dashboard ([0b0273c](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/0b0273cd7ecef9525a0c474b89164119da8bd157))
* **public-endpoints:** add /api/Public to default paths ([6c9dea7](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/6c9dea720ddfb6d9ece5893ff1f40d6ae1a55886))
* **rbac:** add Command Template Sync endpoint mapping ([afcf363](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/afcf363703b89a78e17a27ff1aa89995f57f6c57))
* **rbac:** add DSpace/Quantum types and new endpoints ([6dc1abf](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/6dc1abf4972d2a36ecf7f8a6a2257c288e20bf1e))
* **rbac:** add Heappe command template endpoints ([43f80b1](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/43f80b167ee4747a0738bcb70153dae0087bda15))
* **rbac:** add initial RBAC config for UserOrg.Gateway ([26e7700](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/26e77001d9fd787b3c9e378e976028fbc9c24b3f))
* **rbac:** add ListChangedFilesForJob endpoint ([3e9833b](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/3e9833b4ff46b459639a54cb8d1f5cc78604f26c))
* **rbac:** add method badges and perms to matrix export ([1bcc9f5](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/1bcc9f5cf07a9cabdd9322edbfe9f4459c9b96b4))
* **rbac:** add migrate endpoint and improve HEAppE API ([424c4a0](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/424c4a0ac133e85f6c4e280d87dc94d37c4783a1))
* **rbac:** enforce explicit perms for service accounts ([f8f7973](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/f8f7973a93c9bae8c17d3263ba503df21363eed0))
* **rbac:** improve matrix UI and export for methods/perm ([ab5677e](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/ab5677e2d738769d6b14793c39d58d6ce0d2b7ea))
* **rbac:** update API paths and add jobscript roles ([114b2e9](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/114b2e9b1cd6f52dddeb5843a576fc251d2b7c63))
* **rbac:** update permissions and endpoints mapping ([a58e337](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/a58e3379ff355664785fa3245e829b16a57140c1))
* **rbac:** update RBAC JSON and exclude migrations ([563b51d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/563b51d89b44048442fccf5a2575311493714763))
* **rbac:** update roles, endpoints, and assignment logic ([a3368a6](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/a3368a6d243d8a7318b20106ffc9294e6f32ccd4))
* **rbac+heappe:** improve RBAC, caching, and HEAppE logic ([84707d7](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/84707d73ba8a7ba40461b04cb587f7a46fb9b920)), closes [#72](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/issues/72)
* **release:** enhance release stage with before_script and improved version handling ([28bde03](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/28bde030b05a93ee071c27d63e9e842ea08b59c6))
* **role-management:** add RBAC matrix and HTML export ([5dd214d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/5dd214d6e27d13c2dbed8d25733e23606d34795c))
* **role-management:** add RBAC matrix UI and export ([0b0ae19](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/0b0ae19d8f5b17d24e8fc61df79713d94b2c7261))
* **role-management:** handle setup token via query param ([d0b4b21](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/d0b4b21c3093c72e0711cc0a62940882845878f2))
* **role-management:** support configurable path base ([5f234af](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/5f234afac2f162b67b96b38ded49a2d6377d77d1))
* **role-mgmt:** add persistent state and circuit pause ([ad09934](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/ad09934b7f9cbcfc678199ab29ab66b75da5781b))
* **role-mgmt:** overhaul Blazor UI & RBAC backend ([8d73f97](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/8d73f9760ab419d6f6c01ea67bc0f2896819cc23))
* **role:** add hierarchy and category support ([c4c4b27](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c4c4b273a7cd8f72224b5e3e467c0eb8e5e0fdd2))
* **role:** add hierarchy and category support ([151d57d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/151d57d7f28c0df941e9d383beddb88487cdb437))
* **role:** add import/export for roles & permissions ([2101ef3](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/2101ef30624ab0e1eb51aef10f3a15b860060a87))
* **seed:** add DSpace and Quantum resource types ([88e3f3b](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/88e3f3b52b1a6b0421ccf07da4c809cdfb561c08)), closes [#142](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/issues/142)
* **service:** pass role name instead of ID to assignment ([f49d7d8](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/f49d7d81c758c01593f9afec7aecdb4159eb42ec))
* **service:** use RoleListService for admin checks ([fec9974](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/fec99749c5ee2cb3f9dfc41f117c823f500a2188))
* **sync:** add command template sync endpoint ([503d52b](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/503d52b75760e3d72f5fd0d507ea5d443c9e1bc6))
* **sync:** add project command template sync feature ([d0ef9ea](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/d0ef9ea2a4456b8d4d515e90343c45bb89ebffdf))
* **sync:** enhance HEAppE command template sync logic ([0cd179e](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/0cd179e0a5abaf56c9ac2e5d32926afbb82d3899))
* **sync:** enhance location partition sync & logging ([f4e6b3b](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/f4e6b3b66ccf1adb1fa101c2b9ea21a692087169))
* **sync:** improve filtering and aggregation logic ([01d931e](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/01d931ece587501dac89a9455387c758f2a6d09c))
* **telemetry:** add tracing to consumer loop workers ([a5e873c](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/a5e873c1785e52cc276cfbb3f916d4fd3956a476))
* **tests:** update test structure and dependencies ([b0ef12e](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/b0ef12ec1f2b9784553b78873d865733f8e19702))
* **trace:** expose OpenTelemetry trace headers on API and Gateway responses ([532f264](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/532f264a3ac866d0470af7319e840e6b1099b479))
* **user:** assign admin role to first user created ([52b1a14](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/52b1a14493c5178e940bcbb2d7a199da19dad2ad))
* **user:** integrate Role Service for user roles ([30aa8d7](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/30aa8d7832128b8e82cfa0f3920ec9dc8b125685))
* **userorg:** add project resource sync after migration ([2740a4a](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/2740a4a8d7f8e775867ecaa11909f7e4ae70320d))
* **userorg:** use role service for dynamic role lookup ([f7e8b3d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/f7e8b3d815188441a61954894e464bf5880ab616))
* **vault:** add method to list DDI ServiceAccessKeys and endpoint to read DDI entries\n\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com> ([fef2b93](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/fef2b9331892f9ea33376975b73600498326554d))
* **vault:** integrate HashiCorp Vault and tracing ([7cff7b1](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/7cff7b1e24b1437b63114bb87fc282552965fc8e))
* **version:** add remote resolver with HTTP fallback ([ef37bdd](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/ef37bdd985d201c0e50afe8f141122069069c6ee))

### **🚥 Tests**

* add bUnit and component test for RoleManagementPage; register packages centrally ([e2edfd7](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/e2edfd7b39efd5b2f057a274152c93cbf7168f11))
* add unit tests for CommandTemplateMigrationEndpoint ([a2ef826](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/a2ef8263f00c72dae9a182faf2f426b710c1b6d6))
* allow mock TriggerSynchronizationForProjectResource ([dbfff62](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/dbfff6245d3c357feca0e01f0643dc22b489eadb))
* make all test classes public ([c8a1fe9](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c8a1fe957bc90a1755025aa039e8690866e0b262))
* remove flaky HTML export test, clean up file ([c5981f8](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c5981f897a2136fd7fe60b7d7f88aa20f870ed17))
* remove Forge.Core.EndpointAuth.Tests from solution ([362c582](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/362c582e3a5cff405ee102956f1d6985b361d816))
* remove obsolete JobScript external service test ([d2f2f3a](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/d2f2f3aff9aeadc49dc930a66787794dd70e0015))
* use ICredentialsService in migration endpoint tests ([fffef1c](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/fffef1c67079cf77ebc1d7ec390996d7846e7d8d))
* **ci:** enable Microsoft Testing Platform support in test projects ([f9a977f](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/f9a977f12557da3443c6226728af9d7ba6c5eb07))
* **gateway:** update RoleManagementService unit tests for new signature ([1908149](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/1908149f0908a092be69bf820287ce0ae4d4996f))
* **role-management:** add bUnit test for RoleManagementPage loading reference data ([d6d60fc](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/d6d60fc5a3006747cc1a6eb35090b5890bcf9a6d))
* **role-management:** add unit tests for RoleManagementService and prepare for bUnit work ([90bc1dd](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/90bc1ddfa6951cab5bf0ab1d84a24b85c73e91ce))

### **🚦 CI/CD**

* add -n flag to dotnet-outdated in check stage ([45fe60c](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/45fe60c17aa79efd2d4e69ecdac186917721229e))
* add base Docker images and CI jobs; include Seeds and docs ([b48e45f](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/b48e45f1f67cbc968ed4dd0273ed50ce97bb0fe4))
* add Gateway Dockerfile and use BASE_IMAGE/ VERSION in docker build jobs ([9db4a8e](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/9db4a8e0b2a4feee98b41b01992b81b4e2e2d097))
* allow check stage to run on all branches ([d4bbf96](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/d4bbf9631d8651827a04f6aa044f6c00f0d832df))
* allow failure for dotnet format job ([18c5152](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/18c5152cb533222557ed3c1ec69bcc2d95ad6cb5))
* comment out rules in CI configuration files ([d47bb71](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/d47bb717e44bf3e801051a68bd441477f13beef3))
* ensure junit reports created even when tests fail (capture dotnet test exit code and convert TRX) ([5b07f06](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/5b07f06bfc2ba4927881ebfd47589d9b0f02315f))
* exclude tags from all pipeline stages ([d325774](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/d3257746ad0eec5248c19ad52240b026a3170871))
* exclude tags from triggering pipeline ([bb8a1cb](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/bb8a1cb1c9d1d54f3e44b105ec80ab510e9492c5))
* fix release_version job dependency in docker build pipeline ([f262c01](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/f262c01b36fd04632f82a159d96e4dc708fc766a))
* restrict pipeline execution to develop, testing, and main branches ([9252c4b](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/9252c4bb05c493067c9c1d02aa23505fefbf8d68))
* switch to docker buildx for base image builds ([08d217a](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/08d217ad6a4c76142e5a877a849bae795dcb0a64))
* update buildx driver to docker-container and add latest tag ([464de4f](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/464de4fa869d4a194d2bb5fb75f5ef4da151fa84))
* update default tags in CI configuration ([01848b0](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/01848b02c982ec11005a2d1fb0731d3e91872f4b))
* update default tags in CI configuration ([f1f8c63](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/f1f8c635270d50ee73c800d50e8800bbe3f558c0))
* update Docker build to use commit SHA for tagging ([671fe36](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/671fe36ade22b3862d681241992e6cac93c1d8a3))
* update pipeline rules for merge request events ([e91f226](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/e91f226cbaac4afe1af565985fe9b7e4e137ee97))
* update release job rules and .gitignore backup file ([f1dee3d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/f1dee3d927d336573e700031ba6092927c9854a1))
* use canonical mcr dotnet:10.0 tags (remove bookworm-slim suffix) to fix Docker pulls ([9134ef1](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/9134ef141081ee8cf27e1b21fc93473e993d83c6))
* use VERSION from release.env for tagging; set ENV VERSION in images ([9618087](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/9618087311f7a7778db84cabbd4ce7380bd6bd7d))
* **docker:** change job trigger to manual for builds ([279f1d7](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/279f1d7c239b7025bc0f9cfe21a0abb6031d24ba))
* **docker:** replace buildx with docker build for images ([5800f60](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/5800f602d4f3947500cc213866719b05429699a8))
* **release:** add token verification and project metadata query ([450a4a6](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/450a4a6c662396a5a8c14f32d0d9e6b35f25f4b5))
* **release:** improve release.env creation logic ([cb862d5](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/cb862d57bd508bfd1cddb5be568f91f02a4cb132))
* **release:** remove fallback for semantic-release command ([dad742b](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/dad742bd95707a03d0e1eb792f7ef7fb6f02ed74))
* **release:** simplify release stage and remove debug ([8c43abf](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/8c43abfdf986d9f12b2cf9acd8378a19a3053abb))
* **test:** disable unit_tests on tag, clean csproj ([15a2afa](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/15a2afa1c62c38c9b1ec44323eadc421a4642101))

### **🛠️ Fixes**

* add missing closing braces to code blocks ([4928323](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/4928323bf3e9d6de8c197e677b1e8cda5ac7d64e))
* Added migration ([a57449f](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/a57449fde3d3f843c3082d41e87750db0a4ae649))
* cicd pipeline tests report ([9a9e7db](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/9a9e7db7920a8ef3628402764aa7cc2ec5b0bd5b))
* format User-Agent header ([6dd4485](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/6dd4485a4a9ac52ffe0f272c2a7851dc843d8529))
* improve project allocation & error flow ([62ab217](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/62ab21733fef3f57748b87c5c2d0cf5e1b8f49ca))
* improve URL normalization and user agent ([8672561](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/867256122dbcfdab48883fa07c042dd3c32e8ce0))
* internal visibility ([636641a](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/636641ae8d3a40d6e054ee7072db5cc7c11908c8))
* misplaced attribute ([a03a5ca](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/a03a5cabd2636b86a2bee71e0dae684df341bbcf))
* optimize query with LocationPartition include ([ce3642d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/ce3642d0011988f6788a027cc179505bb2412cca))
* ProjectResource bug ([bbda4e5](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/bbda4e5e6e0600139d5337b5b1bc26e1ec2fcc9a))
* standardize HEAppE URL and version handling ([7ef2843](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/7ef28434bcf4c0727dec93ae60ac476d5b294059))
* support path prefix in role management middleware ([4de240c](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/4de240c73f2718a75fdf25ae2d01addd5475c4c2))
* Temporary set ReadOnly attribute only for EFP ([31fad22](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/31fad223be6bff8d09bd20f3c461e9239625a910))
* update EF config and add migration ([542205e](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/542205e98b266c8de3c6bd7a0adfae4db78978eb))
* update public endpoints and proxy logic ([e6f02bd](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/e6f02bd69bd2d051bb01fcc9048a576fee3a2d24))
* use correct project resource ID ([a4383a7](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/a4383a7047fe7d2b6f200bcabf8e140761fce4fb))
* **/api/DataStaging/WorkerData:** improve JWT claim usage and Vault error handling ([256ae0a](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/256ae0a46bf57a24574c17dadb15d540ad26e6a0))
* **api:** add data seed path and DB context env vars ([9a0780e](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/9a0780e5b109ccbb34f84eb81639ff2f0d9504f3))
* **api:** centralize project resource retrieval logic ([01cc49d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/01cc49dd1bf4b5d39e092abbf68d76f1c7f7ae21)), closes [#369](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/issues/369)
* **api:** centralize user project role retrieval ([fdb7a6e](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/fdb7a6e7f8ed32b6b97e7a26bb1fce769be58305))
* **api:** inject HttpContext for user access ([72dc941](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/72dc941181809dc90a6794062807a5861a9400d8))
* **api:** migrate to new HashiCorpVault integration ([1372425](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/137242570dd712f0d19e002498e2cb38a50cefaf))
* **api:** reorder sync after project resource ops ([695dd64](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/695dd646c29d090685bb3637c3ceb4492f7982ca))
* **api:** role checks and resource access logic ([23c9ae3](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/23c9ae320fae0b9557095661fb2598dfc47b2aad))
* **api:** use IHttpContextAccessor for endpoint DI ([90e92e4](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/90e92e40225964c51c55abd531a9c34b2083eecf))
* **auth:** add provider ID claim to internal tokens ([04a8595](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/04a8595f4113cc92cb33b4f027b973fda30d58a6))
* **auth:** disable authorization on permissions endpoint ([f1a1fee](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/f1a1fee4835efc0f12cd99565e6e3ef988d1bf21))
* **auth:** improve path handling and prefix support ([d51fc54](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/d51fc5463ddd77af3386c0f94148f875b2f40407))
* **auth:** improve role-management redirect handling ([0cad084](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/0cad084bd8372d7a425edb91080d94ea6b306dc4))
* **auth:** remove TokenOrApiKeyEndpointFilter and set GatewayJwt as default; add TODOs for refactor ([54c3ed6](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/54c3ed66baed43976d99387de99797c55f094266))
* **auth:** restrict service perm check to API key users ([38ea5c6](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/38ea5c6be78321b2a86d2f51abd903e226f0dca3))
* **auth:** use full request path for permission checks ([b39a66a](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/b39a66aff3b17a49d31b400400502509b252ab4a))
* **background:** improve DI and worker scoping ([fc9b97b](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/fc9b97bddf193ba672f148dc63ef017bebb4270a))
* **ci:** change app path & cleanup ([51cad60](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/51cad60cabbc50ee384f04a7932889e48f4e83aa))
* **ci:** change buildx driver from docker-container to docker ([69ffeda](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/69ffeda71d93b4921bd192d90211c1b5abe8bea3))
* **ci:** correct docker build and push commands for prod base ([258953f](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/258953f5c57904ebcaad426cb62648d6fdc1c828))
* **ci:** correct docker build stage reference in CI config ([f57a749](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/f57a7499f9a4d32a16d7279dd8b49bdd4e83e570))
* **ci:** correct dotnet-ef installation path and logic ([9604884](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/96048844d6fad05d77e40fd07e1807ed8b8d95e9))
* **ci:** correct PATH export for dotnet tools ([22df537](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/22df537655bf4546584f2246baeb0d20e4bd7be4))
* **ci:** correct paths for dotnet restore and build ([1f0b54a](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/1f0b54a4bcca8a17e083bfa4dbac2ca28023666b))
* **ci:** remove unnecessary rules for manual jobs ([fdb9184](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/fdb918448969f00a00161748b342ed6d1aa4c3cc))
* **ci:** update container repository path for API and Gateway images ([f797c85](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/f797c855725ef63ab272c66dd0bc38c59866e162))
* **ci:** update container repository references in build script ([f02fdb1](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/f02fdb1ce364be72f36be5e3239746077efb0f2d))
* **ci:** update DOTNET_IMAGE to version 10.0-alpine ([8d02727](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/8d02727f40e9d2f7add7a960f66c5bb31fa36b8a))
* **ci:** update test command to remove unnecessary build ([ad12227](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/ad1222787208ea27d8e56813ea700ade11dfd631))
* **docker:** update VERSION argument in Dockerfiles ([c528cf0](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/c528cf0fdba1d6722424b412ac5e3e4e3461aaed))
* **docker:** use dynamic base image for API and Gateway ([cade7bc](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/cade7bc1dd6fab38ceb5be153552d3629287e308))
* **import:** remove Debug.WriteLine, replace Console.WriteLine with ILogger; use ITestOutputHelper in permission tests ([efb4704](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/efb4704d197621f437ca9b1efb2f87862bccb69d))
* **import:** resolve Permission import compile errors and robustly handle JsonElement->DTO deserialization; add fallbacks and tests ([13fb802](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/13fb80262e983eefe1e5e3b6c7bacb713c89cf2d))
* **location:** improve auth and repo logic ([fe35be3](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/fe35be3284c83d0b59521c54efb47aed45d686e3))
* **logging:** always use StepUpMode.AlwaysOn ([cd0f82a](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/cd0f82a3736908a3cc7bc2778897b454fcf7ca02))
* **middleware:** improve path handling and logging ([4965656](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/4965656a69378cab992ca08d53930ccaf2c79a93))
* **migration:** check HEAppE identifier before credentials ([2d345fd](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/2d345fdf89fe10e36eb764e6feec00af6d98d489))
* **migration:** remove script dir prefix from exec command ([07edbe5](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/07edbe59cf547b41ee21116a979ca793488d7a69))
* **migrations:** improve error handling and logging ([6dd39a0](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/6dd39a0c75698cf8538e36fd067aa7778624f44f))
* **Program:** normalize case for script mode check ([b63bdb1](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/b63bdb14468666d55275312136ded817cc091a0e))
* **project-resource:** return empty list if not found ([dc51a6f](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/dc51a6fbad86da1de279b68845cad6ba0da499bd))
* **project-sync:** ensure Project is loaded for sync ([d9889d2](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/d9889d2ba452226f137473e4e470aa07163edc81))
* **project:** handle null HEAppE responses defensively ([a831002](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/a831002a1c529d62de8f25bdbc9d41837e258604))
* **project:** improve soft-delete handling and config ([b217572](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/b217572d2c3531b9129e4449292a4dca998a6c29))
* **projects:** filter active projects and cache templates ([6622c90](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/6622c90be4e1058216df79f5fc75d9fa9d194c2a))
* **provider:** correct PUT endpoint provider ID check ([670bfad](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/670bfadf9f99d60712988728bf3b18cefe87a463))
* **release:** simplify version change handling logic ([058961d](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/058961d2fc20bfbd2c521109d1f26dea27c30655))
* **role-management:** add CTS to RoleManagementPage and pass token to service calls; improve UI state and success feedback ([ab08377](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/ab0837797ba76fac1be23809ac26a8460497e9b3))
* **role-management:** avoid duplicating gateway PathBase in RoleManagement PathPrefix ([3f38d97](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/3f38d975272cd599f1b47af670da38d99dc4d270))
* **role-mgmt:** handle PathBase to avoid URL duplication ([10caec8](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/10caec810ea1eea038ddecaf2f9384955f89f4d4))
* **swagger:** disable custom API key auth in Swagger ([eee476f](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/eee476f782efe471edeccc7789278dc3c18fc0eb))
* **tests:** change class visibilit modifier ([1d078bd](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/1d078bd2624e589e1fc8c82484dee7af215b62cd))
* **userinfo:** add cache tags for user info entries ([1ab3ce5](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/1ab3ce558a4df1a819e8d806500ed07b3bcc637c))
* **userinfo:** await cache GetOrCreateAsync calls ([e015151](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/e015151e517a26e826a69aee966ca09507a24244))
* **userinfo:** unify project role retrieval logic ([fa8c2a5](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/fa8c2a5e677808a2b3f9fbd03a8857d1968845ec))
* **UserInfoExtendedEndpoint:** remove OpenIdService and related interfaces ([653f1dc](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/653f1dcf64eca048b24645cda0a850d787b8f70d))
* **userorg:** improve project role handling and docs ([d6c0943](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/d6c0943e80363022c62ce8937c909fa45e7ec18c))
* **UserPermissionDto:** use out param for roleClaim ([5321b6a](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/5321b6a5f11d5e432c54b052eae0cf8500f37527))
* **xunit:** restore xunit.v3 references and add TargetFramework to Heappe.UnitTests.csproj ([e078872](https://opencode.it4i.eu/lexis-platform/backend/userorg/commit/e078872779a9b21f0b7825f1bee1d88e9b549947))
