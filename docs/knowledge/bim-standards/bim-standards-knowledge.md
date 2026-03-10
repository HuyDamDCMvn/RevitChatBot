# BIM Standards Knowledge Base

## Overview
This document provides a comprehensive reference of international BIM (Building Information Modelling) standards for use by the Revit MEP ChatBot. These standards govern information management, classification, data exchange, and security throughout the lifecycle of built assets.

---

## 1. ISO 19650 Series — BIM Information Management

### ISO 19650-1:2018 — Concepts and Principles
- **Scope:** Defines concepts and principles for information management using BIM throughout the lifecycle of built assets.
- **Key Concepts:**
  - **Information Container:** Named persistent set of information (e.g., a file, a folder, a model).
  - **Common Data Environment (CDE):** Agreed source of information for any given project or asset, used to collect, manage, and disseminate information containers.
  - **CDE States:** Work In Progress (WIP) → Shared → Published → Archived.
  - **Information Requirements Hierarchy:**
    - Organizational Information Requirements (OIR)
    - Asset Information Requirements (AIR)
    - Project Information Requirements (PIR)
    - Exchange Information Requirements (EIR)
  - **Level of Information Need:** Defines the extent and granularity of information required.
  - **Information Model:** Set of structured and unstructured information containers.
    - Project Information Model (PIM) — used during delivery phase
    - Asset Information Model (AIM) — used during operational phase
  - **Appointing Party:** Entity that appoints others to carry out work.
  - **Appointed Party:** Entity appointed to carry out work.
  - **Lead Appointed Party:** Coordinates the delivery team.
  - **Delivery Team:** Collection of task teams under a lead appointed party.
- **Relevance to Revit MEP:**
  - Defines how MEP model information should be organized and exchanged.
  - CDE workflow applies to all Revit model sharing.
  - Information requirements drive what parameters and detail levels are needed in MEP models.

### ISO 19650-2:2018 — Delivery Phase of Assets
- **Scope:** Specifies requirements for information management during the delivery phase (design, construction, handover).
- **Key Processes (8 stages):**
  1. **Assessment and Need:** Establish project information requirements, CDE, information standard, information protocol.
  2. **Invitation to Tender:** Define Exchange Information Requirements (EIR), acceptance criteria.
  3. **Tender Response:** BIM Execution Plan (BEP), capability assessment, mobilization plan, risk register.
  4. **Appointment:** Confirm BEP, detailed responsibility matrix, Task Information Delivery Plan (TIDP), Master Information Delivery Plan (MIDP).
  5. **Mobilization:** Resource mobilization, IT setup, test production methods.
  6. **Collaborative Production:** Generate information, QA checks, review and approve for sharing, model review.
  7. **Information Model Delivery:** Submit for authorization → review → submit for acceptance.
  8. **Project Close-Out:** Archive PIM, capture lessons learned.
- **Key Documents:**
  - BIM Execution Plan (BEP)
  - Exchange Information Requirements (EIR)
  - Master Information Delivery Plan (MIDP)
  - Task Information Delivery Plan (TIDP)
  - Responsibility Matrix
- **UK National Annex (BS EN):**
  - Information container ID convention: `Project-Originator-Volume/System-Level/Location-Type-Role-Number`
  - Delimiter: Hyphen (U+002D)
  - Status codes: S0 (WIP), S1-S7 (Shared), A1-An (Published), CR (As-constructed)
  - Role codes: A=Architect, E=Electrical, M=Mechanical, P=Public Health, S=Structural
  - Revision: P01.01 (Preliminary), C01 (Contractual)
- **Relevance to Revit MEP:**
  - Governs how MEP design information is produced, reviewed, and delivered.
  - File naming conventions apply to Revit models.
  - QA/QC processes apply to model checking before sharing.

### ISO 19650-3:2020 — Operational Phase of Assets
- **Scope:** Requirements for information management during the operational phase (maintenance, facility management).
- **Key Concepts:**
  - Asset Information Model (AIM) maintenance and updating.
  - Trigger events for information management (planned maintenance, reactive maintenance, asset disposal).
  - Links to enterprise systems (CAFM, CMMS, ERP).
  - Processes mirror Part 2 but adapted for operations: assessment, tender, response, appointment, mobilization, production, delivery.
- **Relevance to Revit MEP:**
  - MEP models must contain operational information (equipment schedules, maintenance data, manufacturer info).
  - As-built models must be accurate for facility management.
  - Parameter completeness for MEP equipment is critical for AIM.

### ISO 19650-4:2022 — Information Exchange
- **Scope:** Specifies process and criteria for individual information exchanges within the ISO 19650 framework.
- **Key Concepts:**
  - **Information Exchange Process:** Mobilization → Shared State → Published State → Change Actions.
  - **Decision Points:**
    - Decision A: Approve for sharing (within delivery team).
    - Decision B: Authorize and accept for publication (to appointing party).
  - **Review Criteria (7 Cs):**
    - CDE Compliance — information in correct CDE location with correct metadata
    - Conformance — complies with information standard and production methods
    - Continuity — maintains relationships with previously exchanged information
    - Communication — correct format for intended recipient
    - Consistency — no internal conflicts or contradictions
    - Completeness — all required information present at required level of detail
    - Other criteria (optional) — sustainability, regulatory compliance
  - **Open Data Formats:** Promotes IFC (ISO 16739), BCF, gbXML, COBie.
- **Relevance to Revit MEP:**
  - Defines quality criteria when exchanging MEP models (e.g., IFC export).
  - Clash detection results should be reviewed before model publication.
  - MEP coordination models must meet completeness and consistency criteria.

### ISO 19650-5:2020 — Security-Minded Approach
- **Scope:** Specifies approach to managing security of sensitive information in BIM environments.
- **Key Concepts:**
  - **Sensitivity Assessment:** Determine if built asset information requires security measures.
  - **Security Triage Process:** Classify information sensitivity levels.
  - **Security Strategy:** Risk assessment, mitigation measures, residual risk management.
  - **Security Management Plan:** Personnel security, physical security, technical controls, access management.
  - **Security Breach Management:** Discovery, containment, recovery, review.
  - **Security Risks Include:** Terrorism, espionage, sabotage, theft, unauthorized access to building systems.
- **Relevance to Revit MEP:**
  - Critical infrastructure MEP systems (data centers, hospitals, government) require security classification.
  - Access control for MEP models containing sensitive information.
  - Security considerations for BMS/BAS system data in models.

---

## 2. ISO 29481 Series — Information Delivery Manual (IDM)

### ISO 29481-1:2025 — Methodology and Format
- **Scope:** Prescribes methodology for documenting use cases, business contexts, and exchange requirements.
- **Key Concepts:**
  - **Information Delivery Manual (IDM):** Specification of a use case using business context maps and exchange requirements.
  - **IDM Components:**
    - Use Case description (UC)
    - Business Context Maps (process maps using BPMN, interaction maps using UML)
    - Exchange Requirements (ER) — collection of information units
  - **Information Units:** Description of a piece of information (e.g., room depth, pipe diameter, equipment ID).
  - **Information Constraints:** Data types, rules, and restrictions on information units.
  - **Exchange View Definition (EVD):** Computer-interpretable representation of required data model subset.
  - **Standard Project Phases (ISO 22263):** Inception → Brief → Design → Production → Maintenance → Demolition.
  - **Actors and Roles:** Person/organization involved in business processes (client, designer, engineer, contractor).
- **Relevance to Revit MEP:**
  - Defines what MEP information should be exchanged at each project phase.
  - IDM methodology can specify MEP-specific exchange requirements (e.g., duct sizing data, pipe specifications).
  - BPMN process maps can model MEP coordination workflows.

### ISO 29481-2:2025 — Interaction Framework
- **Scope:** Specifies schema for computer-interpretable interaction frameworks for digital IDM communication.
- **Key Concepts:**
  - **Interaction Framework:** Formal schema for digital IDM communication, defining roles, transactions, messages, and data elements.
  - **Digital IDM Communication:** Structured electronic exchange of information conforming to IDM.
  - **Transaction Pattern:** Request → Execute → Respond → Accept/Reject.
  - **Message Types:** Request, promise, state, accept, reject, revoke, decline, quit.
  - **XML Schema:** Defines interaction framework and message schemas in EXPRESS/XSD.
  - **Advanced Electronic Signatures:** Support for message authentication and non-repudiation.
- **Relevance to Revit MEP:**
  - Automates MEP information exchange processes.
  - Structured communication for RFIs, design changes, coordination issues.

### ISO 29481-3:2022 — Data Schema
- **Scope:** Defines specification to store, exchange, and read IDM specifications in machine-readable format.
- **Key Concepts:**
  - Machine-readable IDM representation using XML.
  - Supports automated validation of exchange requirements.
  - Links IDM components (use cases, business context maps, exchange requirements) in structured format.

---

## 3. ISO 12006 Series — Construction Information Organization

### ISO 12006-2:2015 — Framework for Classification
- **Scope:** Defines framework for construction-sector classification systems.
- **Key Concepts:**
  - **Construction Object Classes:**
    - **Resources:** Construction products, construction aids, construction agents, construction information.
    - **Processes:** Pre-design, design, production, maintenance processes.
    - **Results:** Construction complexes → entities → elements; built spaces, zones, work results.
    - **Properties:** Physical, functional, spatial, temporal, compositional, cultural, administrative.
  - **Classification Principles:**
    - Type-of relation (classification hierarchy): subclasses are types of superordinate classes.
    - Part-of relation (composition hierarchy): subordinates are parts of a whole.
  - **Recommended Classification Tables (12 tables):**
    - Construction information (by content)
    - Construction products (by function/form/material)
    - Construction agents (by discipline/role)
    - Construction aids (by function/form/material)
    - Management (by management activity)
    - Construction process (by activity/lifecycle stage)
    - Construction complexes (by form/function/user activity)
    - Construction entities (by form/function/user activity)
    - Built spaces (by form/function/user activity)
    - Construction elements (by function/form/position) — e.g., wall, roof, floor, HVAC, drainage, electrical, fire protection systems
    - Work results (by work activity and resources)
    - Construction properties (by property type)
  - **National Implementations:** Uniclass (UK), OmniClass (US), CoClass (Sweden).
- **Relevance to Revit MEP:**
  - Classification of MEP elements (HVAC systems, plumbing, electrical) follows this framework.
  - Revit categories map to construction element classification.
  - Uniclass/OmniClass codes can be assigned to Revit families.

### ISO 12006-3:2022 — Framework for Object-Oriented Information
- **Scope:** Framework for defining taxonomies (dictionaries) for the construction industry.
- **Key Concepts:**
  - Defines data model for construction object dictionaries.
  - Supports buildingSMART Data Dictionary (bSDD).
  - Enables linking of terms, properties, and classifications across different systems and languages.
  - Object-oriented approach: classes, properties, relationships.
- **Relevance to Revit MEP:**
  - bSDD integration for standardized MEP property definitions.
  - Cross-language terminology mapping (e.g., EN-VI for MEP terms).

---

## 4. ISO 7817-1:2024 — Level of Information Need

- **Scope:** Defines concepts and principles for specifying level of information need for BIM deliverables.
- **Key Concepts:**
  - **Level of Information Need:** Framework to specify the extent and granularity of information.
  - **Aspects:**
    - Geometrical information (detail level, dimensionality, location, appearance, parametric behavior)
    - Alphanumeric information (identification, properties, documentation)
  - **Prerequisites:** Purpose, information delivery milestone, object type, actor delivering.
  - **Distinction from LOD/LOI:** Level of information need is purpose-driven, not a fixed scale.
- **Relevance to Revit MEP:**
  - Specifies what level of detail MEP elements need at each project stage.
  - E.g., concept design: schematic routing; detailed design: exact sizes, materials, connections.
  - Drives parameter completeness requirements for MEP families.

---

## 5. ISO 12911:2023 — Framework for BIM Implementation Specification

- **Scope:** Defines systematic approach for developing information management documents as structured specifications.
- **Key Concepts:**
  - Framework for creating BIM-related specifications that can support automated checking.
  - Applies to both process specifications and information specifications.
  - Supports compliance checking and validation of BIM deliverables.
- **Relevance to Revit MEP:**
  - Automated model checking rules for MEP (parameter validation, clash detection rules).
  - Specification of QA/QC requirements for MEP models.

---

## 6. ISO 23386:2020 — Data Templates for Construction Objects

- **Scope:** Methodology for describing, creating, and maintaining properties and data templates.
- **Key Concepts:**
  - **Data Template:** Collection of properties that describe a construction object type.
  - **Property:** Characteristic of a construction object (e.g., power rating, flow rate, material).
  - **Groups of Properties:** Organized sets of related properties.
  - **Interconnected Data Dictionaries:** Linking properties across different dictionaries.
  - **Governance:** Rules for creation, validation, and maintenance of properties.
- **Relevance to Revit MEP:**
  - Standardizes MEP equipment property definitions (e.g., pump data template with flow rate, head, power, efficiency).
  - Revit shared parameters can align with ISO 23386 data templates.
  - Supports product data exchange between manufacturers and designers.

---

## 7. ISO 23387:2025 — Data Templates for Construction Objects (Properties)

- **Scope:** Specifies how to use data templates conforming to ISO 23386 for construction product/object properties.
- **Key Concepts:**
  - Practical application of data templates for specific construction objects.
  - Linking data templates to classification systems (ISO 12006-2).
  - Machine-readable property definitions for automated processing.
  - Support for product comparison and specification verification.
- **Relevance to Revit MEP:**
  - Equipment selection verification (e.g., AHU data template → Revit family parameters).
  - Automated checking of product data against specifications.

---

## 8. DIN 276 — Building Costs

- **Scope:** German standard for classification and estimation of building costs throughout the lifecycle.
- **Key Concepts:**
  - **Cost Groups (Kostengruppen):**
    - KG 100: Land/site (Grundstück)
    - KG 200: Preparation (Herrichten und Erschließen)
    - KG 300: Building construction (Bauwerk – Baukonstruktionen)
    - KG 400: Building services (Bauwerk – Technische Anlagen) — **MEP relevant**
      - KG 410: Sewage, water, gas installations
      - KG 420: Heating installations
      - KG 430: Ventilation/AC installations
      - KG 440: Electrical installations
      - KG 450: Communication/IT installations
      - KG 460: Conveying installations
      - KG 470: Use-specific installations
      - KG 480: Building automation
    - KG 500: Outdoor facilities
    - KG 600: Equipment and artwork
    - KG 700: Ancillary construction costs
    - KG 800: Financing
  - **Cost Estimation Levels:** DIN 276 defines increasingly precise estimates from feasibility through detailed design.
  - **Cost Benchmarking:** KG structure enables comparison across projects.
- **Relevance to Revit MEP:**
  - MEP cost estimation structured by KG 400 subgroups.
  - Revit element quantities can be mapped to DIN 276 cost groups.
  - MEP system classification aligns with KG 410-480.

---

## 9. Additional Standards (Unidentified PDFs: 9516346, 9553502)

These appear to be ISO/DIN standards related to BIM or construction information management. They are included in the knowledge base for future reference and will be indexed when PDF content extraction identifies their specific standard numbers.

---

## Cross-Reference Matrix: Standards vs. BIM Workflow

| Workflow Phase | Primary Standards | Key Requirements |
|---|---|---|
| **Project Setup** | ISO 19650-1, ISO 19650-2 (5.1) | OIR, AIR, PIR, EIR, CDE setup |
| **BIM Execution Planning** | ISO 19650-2 (5.3-5.4) | BEP, MIDP, TIDP, Responsibility Matrix |
| **Design & Modelling** | ISO 7817-1, ISO 12006-2, ISO 29481-1 | Level of info need, classification, exchange requirements |
| **MEP Coordination** | ISO 19650-4, ISO 29481-1 | 7C review criteria, clash detection, IDM |
| **Data Management** | ISO 23386, ISO 23387, ISO 12006-3 | Data templates, property definitions, dictionaries |
| **Quality Assurance** | ISO 12911, ISO 19650-4 | Automated checking, conformance, completeness |
| **Information Exchange** | ISO 19650-4, ISO 29481-2 | Open formats (IFC, BCF), interaction framework |
| **Handover** | ISO 19650-2 (5.7-5.8), ISO 19650-3 | PIM → AIM transfer, as-built requirements |
| **Operations** | ISO 19650-3, ISO 19650-5 | AIM maintenance, trigger events, security |
| **Cost Management** | DIN 276 | KG 400 MEP cost classification |

---

## Key Terminology (EN/DE/VI)

| English | German | Vietnamese | Standard |
|---|---|---|---|
| Common Data Environment | Gemeinsame Datenumgebung | Môi trường dữ liệu chung | ISO 19650-1 |
| Information Container | Informationscontainer | Bộ chứa thông tin | ISO 19650-1 |
| Exchange Information Requirements | Austausch-Informationsanforderungen | Yêu cầu trao đổi thông tin | ISO 19650-2 |
| BIM Execution Plan | BIM-Abwicklungsplan | Kế hoạch triển khai BIM | ISO 19650-2 |
| Level of Information Need | Informationsbedarfstiefe | Mức độ nhu cầu thông tin | ISO 7817-1 |
| Information Delivery Manual | Informationslieferungshandbuch | Sổ tay giao nhận thông tin | ISO 29481-1 |
| Data Template | Datenvorlage | Mẫu dữ liệu | ISO 23386 |
| Construction Element | Bauelement | Cấu kiện xây dựng | ISO 12006-2 |
| Building Services | Technische Anlagen | Hệ thống kỹ thuật tòa nhà | DIN 276 |
| Asset Information Model | Bestandsinformationsmodell | Mô hình thông tin tài sản | ISO 19650-3 |
