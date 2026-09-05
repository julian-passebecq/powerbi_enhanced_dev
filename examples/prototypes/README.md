# Local prototypes

These are original PbiBench examples, supplied under the MIT license in `dax-package/LICENSE.txt`. No Databricks connection or package feed is needed.

Open `orders.metric-view.yaml` in **Model tools → Prototypes → Semantic compiler**, compile its intent, and explicitly select a model table containing numeric `Amount` before reviewing the proposed measures. The source name is illustrative. Validate real data semantics separately.

Open the `dax-package` folder in **DAX packages** and review its declared MIT license, raw-file SHA-256 and UDF body before installation. Functions require model compatibility level 1702 or later. A model's package lock changes in the same Undo batch as its functions; export the lock JSON explicitly into a project to review it in Git. There are no installer hooks or executable payloads.
