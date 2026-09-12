# NawiriPro integration boundary

NawiriPro is a separately licensed proprietary product. Its adapter and WPF/API integration belong exclusively in the private product repository.

The private host authenticates the existing company, active branch, staff member and rights. It maps approved existing business reports into `BusinessDataResult`, uses server-managed encrypted provider settings, and presents scoped verified results through its NawiriAI desktop experience. Existing report/export workflows remain product responsibilities.

This repository intentionally contains no proprietary queries, schema mappings, database migrations, licensing checks, payment integrations, financial scoring, production configuration or copied product history.

For local development, the private host may reference these projects through a configurable root path. Released integrations should pin published package versions. NawiriPro uses NawiriAI; NawiriAI does not require NawiriPro.
