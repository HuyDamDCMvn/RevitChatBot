# Revit BuiltInParameter Complete Reference — MEP Focus

Comprehensive reference of BuiltInParameter enum values used in MEP engineering.

## Duct Parameters

| BuiltInParameter | Display Name | Storage | Internal Unit | Category |
|-----------------|-------------|---------|---------------|----------|
| `RBS_DUCT_FLOW_PARAM` | Flow | Double | ft³/s | Ducts |
| `RBS_VELOCITY` | Velocity | Double | ft/s | Ducts |
| `RBS_CURVE_WIDTH_PARAM` | Width | Double | feet | Ducts (rect) |
| `RBS_CURVE_HEIGHT_PARAM` | Height | Double | feet | Ducts (rect) |
| `RBS_CURVE_DIAMETER_PARAM` | Diameter | Double | feet | Ducts (round) |
| `RBS_CALCULATED_SIZE` | Size | String | — | Ducts |
| `RBS_DUCT_SYSTEM_TYPE_PARAM` | System Type | ElementId | — | Ducts |
| `RBS_LOSS_COEFFICIENT` | Loss Coefficient | Double | — | Duct Fittings |
| `RBS_FRICTION` | Friction | Double | internal | Ducts |
| `RBS_PRESSURE_DROP` | Pressure Drop | Double | internal | Ducts |
| `RBS_REYNOLDSNUMBER_PARAM` | Reynolds Number | Double | — | Ducts |
| `RBS_ROUGHNESS` | Roughness | Double | feet | Duct Types |
| `RBS_HYDRAULIC_DIAMETER_PARAM` | Hydraulic Diameter | Double | feet | Ducts |
| `RBS_EQ_DIAMETER_PARAM` | Equivalent Diameter | Double | feet | Ducts (rect) |

## Pipe Parameters

| BuiltInParameter | Display Name | Storage | Internal Unit | Category |
|-----------------|-------------|---------|---------------|----------|
| `RBS_PIPE_DIAMETER_PARAM` | Diameter | Double | feet | Pipes |
| `RBS_PIPE_VELOCITY_PARAM` | Velocity | Double | ft/s | Pipes |
| `RBS_PIPE_FLOW_PARAM` | Flow | Double | ft³/s | Pipes |
| `RBS_PIPE_SLOPE` | Slope | Double | ratio (0.01 = 1%) | Pipes |
| `RBS_PIPING_SYSTEM_TYPE_PARAM` | System Type | ElementId | — | Pipes |
| `RBS_PIPE_INNER_DIAM_PARAM` | Inside Diameter | Double | feet | Pipes |
| `RBS_PIPE_OUTER_DIAMETER` | Outside Diameter | Double | feet | Pipes |
| `RBS_PIPE_FRICTION_FACTOR_PARAM` | Friction Factor | Double | — | Pipes |
| `RBS_PIPE_FRICTION_PARAM` | Friction | Double | internal | Pipes |
| `RBS_PIPE_PRESSURE_DROP_PARAM` | Pressure Drop | Double | internal | Pipes |
| `RBS_PIPE_REYNOLDS_NUMBER_PARAM` | Reynolds Number | Double | — | Pipes |
| `RBS_PIPE_ROUGHNESS_PARAM` | Roughness | Double | feet | Pipe Types |
| `RBS_PIPE_INVERT_ELEVATION` | Invert Elevation | Double | feet | Pipes |

## Electrical Parameters

| BuiltInParameter | Display Name | Storage | Internal Unit | Category |
|-----------------|-------------|---------|---------------|----------|
| `RBS_ELEC_APPARENT_LOAD` | Apparent Load | Double | VA | Elec Equipment |
| `RBS_ELEC_TRUE_LOAD` | True Load | Double | W | Elec Equipment |
| `RBS_ELEC_VOLTAGE` | Voltage | Double | V | Elec Equipment |
| `RBS_ELEC_NUMBER_OF_POLES` | Number of Poles | Integer | — | Elec Equipment |
| `RBS_ELEC_CIRCUIT_NUMBER` | Circuit Number | String | — | Elec Elements |
| `RBS_ELEC_PANEL_NAME` | Panel Name | String | — | Elec Elements |
| `RBS_CTC_BOTTOM_ELEVATION` | Bottom Elevation | Double | feet | Cable Trays |
| `RBS_CTC_TOP_ELEVATION` | Top Elevation | Double | feet | Cable Trays |
| `RBS_CONDUIT_DIAMETER_PARAM` | Conduit Diameter | Double | feet | Conduits |
| `RBS_CONDUIT_INNER_DIAM_PARAM` | Inner Diameter | Double | feet | Conduits |

## Common MEP Parameters (All Categories)

| BuiltInParameter | Display Name | Storage | Notes |
|-----------------|-------------|---------|-------|
| `CURVE_ELEM_LENGTH` | Length | Double (ft) | Any MEP curve |
| `RBS_CALCULATED_SIZE` | Size | String | Formatted "400x300" or "Ø250" |
| `RBS_SYSTEM_NAME_PARAM` | System Name | String | System assignment |
| `RBS_SYSTEM_CLASSIFICATION_PARAM` | System Classification | String | SupplyAir, Sanitary, etc. |
| `RBS_START_LEVEL_PARAM` | Reference Level | ElementId | Level association |
| `RBS_OFFSET_PARAM` | Offset | Double (ft) | Offset from level |
| `RBS_REFERENCE_INSULATION_TYPE` | Insulation Type | String | Insulation reference |
| `RBS_REFERENCE_INSULATION_THICKNESS` | Insulation Thickness | Double (ft) | Insulation size |
| `RBS_REFERENCE_LINING_TYPE` | Lining Type | String | Duct lining reference |
| `RBS_REFERENCE_LINING_THICKNESS` | Lining Thickness | Double (ft) | Lining size |

## General Element Parameters

| BuiltInParameter | Display Name | Storage | Notes |
|-----------------|-------------|---------|-------|
| `ALL_MODEL_MARK` | Mark | String | Instance mark |
| `ALL_MODEL_INSTANCE_COMMENTS` | Comments | String | Instance comments |
| `ALL_MODEL_TYPE_COMMENTS` | Type Comments | String | On ElementType |
| `ELEM_FAMILY_AND_TYPE_PARAM` | Family and Type | ElementId | Type reference |
| `ELEM_FAMILY_PARAM` | Family | ElementId | Family reference |
| `ELEM_TYPE_PARAM` | Type | ElementId | Type reference |
| `ELEM_CATEGORY_PARAM` | Category | ElementId | Category |
| `ELEM_CATEGORY_PARAM_MT` | Category | ElementId | Multi-category |
| `DESIGN_OPTION_ID` | Design Option | ElementId | — |
| `PHASE_CREATED` | Phase Created | ElementId | — |
| `PHASE_DEMOLISHED` | Phase Demolished | ElementId | — |

## Room / Space Parameters

| BuiltInParameter | Display Name | Storage | Notes |
|-----------------|-------------|---------|-------|
| `ROOM_NAME` | Name | String | Room/Space name |
| `ROOM_NUMBER` | Number | String | Room/Space number |
| `ROOM_AREA` | Area | Double (sq ft) | Calculated area |
| `ROOM_VOLUME` | Volume | Double (cu ft) | Calculated volume |
| `ROOM_PERIMETER` | Perimeter | Double (ft) | Room perimeter |
| `ROOM_LEVEL_ID` | Level | ElementId | Room level |
| `ROOM_UPPER_LEVEL` | Upper Limit | ElementId | Upper level |
| `ROOM_LOWER_OFFSET` | Base Offset | Double (ft) | From level |
| `ROOM_UPPER_OFFSET` | Limit Offset | Double (ft) | From upper level |
| `ROOM_HEIGHT` | Unbounded Height | Double (ft) | Room height |
| `ROOM_DEPARTMENT` | Department | String | — |
| `ROOM_OCCUPANCY` | Occupancy | String | — |

## Space-Specific Parameters (MEP Spaces)

| BuiltInParameter | Display Name | Storage |
|-----------------|-------------|---------|
| `ROOM_ACTUAL_SUPPLY_AIRFLOW_PARAM` | Actual Supply Airflow | Double |
| `ROOM_ACTUAL_RETURN_AIRFLOW_PARAM` | Actual Return Airflow | Double |
| `ROOM_ACTUAL_EXHAUST_AIRFLOW_PARAM` | Actual Exhaust Airflow | Double |
| `ROOM_DESIGN_SUPPLY_AIRFLOW_PARAM` | Design Supply Airflow | Double |
| `ROOM_DESIGN_RETURN_AIRFLOW_PARAM` | Design Return Airflow | Double |
| `ROOM_DESIGN_EXHAUST_AIRFLOW_PARAM` | Design Exhaust Airflow | Double |
| `ROOM_DESIGN_HEATING_LOAD_PARAM` | Design Heating Load | Double |
| `ROOM_DESIGN_COOLING_LOAD_PARAM` | Design Cooling Load | Double |
| `ROOM_CALCULATED_SUPPLY_AIRFLOW_PARAM` | Calculated Supply Airflow | Double |

## View Parameters

| BuiltInParameter | Display Name | Storage |
|-----------------|-------------|---------|
| `VIEW_SCALE` | View Scale | Integer (denominator, e.g. 100) |
| `VIEW_SCALE_PULLDOWN_METRIC` | Scale | Integer |
| `VIEW_DETAIL_LEVEL` | Detail Level | Integer (1=Coarse, 2=Medium, 3=Fine) |
| `VIEW_NAME` | View Name | String |
| `VIEW_DESCRIPTION` | Title on Sheet | String |
| `VIEWER_VOLUME_OF_INTEREST_CROP` | Scope Box | ElementId |
| `VIEW_PHASE` | Phase | ElementId |
| `VIEW_TEMPLATE_FOR_SCHEDULE` | View Template | ElementId |

## Level Parameters

| BuiltInParameter | Display Name | Storage |
|-----------------|-------------|---------|
| `LEVEL_ELEV` | Elevation | Double (ft) |
| `LEVEL_NAME` | Name | String |
| `LEVEL_IS_BUILDING_STORY` | Building Story | Integer (0/1) |
| `LEVEL_RELATIVE_BASE_TYPE` | Elevation Base | Integer |

## Wall Parameters

| BuiltInParameter | Display Name | Storage |
|-----------------|-------------|---------|
| `WALL_BASE_CONSTRAINT` | Base Constraint | ElementId (Level) |
| `WALL_TOP_OFFSET` | Top Offset | Double (ft) |
| `WALL_BASE_OFFSET` | Base Offset | Double (ft) |
| `WALL_USER_HEIGHT_PARAM` | Unconnected Height | Double (ft) |
| `WALL_ATTR_WIDTH_PARAM` | Width | Double (ft) |
| `WALL_STRUCTURAL_SIGNIFICANT` | Structural | Integer (0/1) |

## BuiltInCategory Reference (MEP)

| BuiltInCategory | Description |
|----------------|-------------|
| `OST_DuctCurves` | Ducts |
| `OST_PipeCurves` | Pipes |
| `OST_Conduit` | Conduits |
| `OST_CableTray` | Cable Trays |
| `OST_FlexDuctCurves` | Flex Ducts |
| `OST_FlexPipeCurves` | Flex Pipes |
| `OST_DuctFitting` | Duct Fittings |
| `OST_PipeFitting` | Pipe Fittings |
| `OST_ConduitFitting` | Conduit Fittings |
| `OST_CableTrayFitting` | Cable Tray Fittings |
| `OST_DuctAccessory` | Duct Accessories |
| `OST_PipeAccessory` | Pipe Accessories |
| `OST_DuctTerminal` | Air Terminals |
| `OST_MechanicalEquipment` | Mechanical Equipment |
| `OST_ElectricalEquipment` | Electrical Equipment |
| `OST_ElectricalFixtures` | Electrical Fixtures |
| `OST_LightingFixtures` | Lighting Fixtures |
| `OST_LightingDevices` | Lighting Devices |
| `OST_PlumbingFixtures` | Plumbing Fixtures |
| `OST_Sprinklers` | Sprinklers |
| `OST_MEPSpaces` | MEP Spaces |
| `OST_Rooms` | Rooms |
| `OST_DuctInsulations` | Duct Insulations |
| `OST_PipeInsulations` | Pipe Insulations |
| `OST_DuctLinings` | Duct Linings |
| `OST_FabricationDuctwork` | Fabrication Ductwork |
| `OST_FabricationPipework` | Fabrication Pipework |
| `OST_FireAlarmDevices` | Fire Alarm Devices |
| `OST_CommunicationDevices` | Communication Devices |
| `OST_DataDevices` | Data Devices |
| `OST_SecurityDevices` | Security Devices |
| `OST_NurseCallDevices` | Nurse Call Devices |
