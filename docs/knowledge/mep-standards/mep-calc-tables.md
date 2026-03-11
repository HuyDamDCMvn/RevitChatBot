# MEP Calculation Reference Tables / Bảng tra tính toán MEP

## 1. Equal Friction Duct Sizing Table

| Airflow (L/s) | Max Velocity (m/s) | Recommended Size (W×H mm) | Equiv. Diameter (mm) |
|---|---|---|---|
| 50 | 4.0 | 200×150 | 170 |
| 100 | 5.0 | 250×200 | 220 |
| 200 | 6.0 | 350×250 | 290 |
| 500 | 7.0 | 500×350 | 410 |
| 1000 | 8.0 | 700×400 | 520 |
| 2000 | 9.0 | 900×500 | 660 |
| 5000 | 10.0 | 1200×800 | 960 |

### Friction Rate Target: 0.8–1.5 Pa/m for low-velocity systems

## 2. Occupancy Density (ASHRAE 62.1 Table 6-1)

| Space Type | m²/person | Rp (L/s per person) | Ra (L/s per m²) |
|---|---|---|---|
| Office | 5 | 2.5 | 0.3 |
| Conference Room | 2 | 2.5 | 0.3 |
| Lobby | 10 | 2.5 | 0.3 |
| Classroom | 4 | 3.8 | 0.3 |
| Retail | 7 | 3.8 | 0.6 |
| Restaurant | 1.4 | 3.8 | 0.9 |
| Hospital Patient Room | 10 | 2.5 | 0.3 |
| Hotel Room | 10 | 2.5 | 0.3 |
| Gymnasium | 4 | 10.0 | 0.9 |
| Kitchen (commercial) | 10 | 3.8 | 0.6 |
| Parking Garage | — | — | 3.8 |

### TCVN 5687 — Outdoor Air Requirements
| Loại phòng | m³/h per person |
|---|---|
| Văn phòng | 25 |
| Phòng họp | 30 |
| Lớp học | 25 |
| Bệnh viện (phòng bệnh) | 40 |
| Nhà hàng | 30 |
| Phòng gym | 60 |
| Bãi đỗ xe | 6 ACH |

## 3. Hazen-Williams C-Factors

| Pipe Material | C-Factor | Notes |
|---|---|---|
| Steel (new) | 120 | Standard |
| Steel (corroded, 10y) | 100 | Use for existing systems |
| Copper | 140 | |
| PVC / CPVC | 150 | |
| Ductile Iron | 130 | |
| Cast Iron (new) | 130 | |
| Cast Iron (old) | 100 | |
| Stainless Steel | 140 | |
| PE/HDPE | 150 | |
| FRP | 150 | |

## 4. Darcy Friction Factor (f) Approximations

| Surface | f (Darcy) | Reynolds Range |
|---|---|---|
| Sheet metal duct (galv.) | 0.018–0.022 | Turbulent |
| Fiberglass duct | 0.025–0.030 | |
| Concrete duct | 0.030–0.040 | |
| Steel pipe (new) | 0.020–0.025 | |
| Copper pipe | 0.015–0.020 | |
| PVC pipe | 0.015–0.018 | |

### Colebrook-White (for exact calculation):
```
1/√f = -2 × log₁₀(ε/(3.7D) + 2.51/(Re×√f))
ε (roughness): steel=0.046mm, copper=0.0015mm, PVC=0.0015mm, galv.sheet=0.15mm
```

## 5. Fitting K-Factor Table

| Fitting Type | K-Factor Range | Typical |
|---|---|---|
| 90° Elbow (smooth) | 0.2–0.4 | 0.3 |
| 90° Elbow (mitered) | 1.0–1.5 | 1.2 |
| 45° Elbow | 0.1–0.2 | 0.15 |
| Tee (straight-through) | 0.2–0.5 | 0.3 |
| Tee (branch) | 0.5–1.8 | 1.0 |
| Reducer (gradual) | 0.02–0.1 | 0.05 |
| Expansion | 0.3–0.8 | 0.5 |
| Gate Valve (open) | 0.1–0.2 | 0.15 |
| Butterfly Valve (open) | 0.3–0.5 | 0.4 |
| Check Valve (swing) | 1.0–2.5 | 1.5 |
| Ball Valve (open) | 0.05–0.1 | 0.05 |
| Strainer | 2.0–5.0 | 3.0 |
| Balancing Valve | 2.0–8.0 | 4.0 |

## 6. Insulation Thickness Table (TCVN / ASHRAE 90.1)

### Chilled Water Pipes (7–12°C):
| Pipe DN (mm) | Min Thickness (mm) | Material |
|---|---|---|
| DN15–25 | 25 | PE Foam / Rubber |
| DN32–50 | 30 | PE Foam / Rubber |
| DN65–100 | 40 | PE Foam / Rubber |
| DN125–200 | 50 | PE Foam / Rubber |
| DN250+ | 60 | PE Foam / Rubber |

### Hot Water Pipes (60–80°C):
| Pipe DN (mm) | Min Thickness (mm) | Material |
|---|---|---|
| DN15–25 | 25 | Mineral Wool |
| DN32–80 | 40 | Mineral Wool |
| DN100–200 | 50 | Mineral Wool |
| DN250+ | 60 | Mineral Wool |

### Supply Air Ducts:
| Location | Min Thickness (mm) | Material |
|---|---|---|
| Indoor | 25 | PE Foam / Rubber |
| Outdoor | 50 | Mineral Wool + Cladding |
| AHU Plenum | 25 | PE Foam |

## 7. Sprinkler Hydraulic Tables (NFPA 13)

### K-Factor Table:
| K-Factor | Nominal Orifice | Flow at 0.7 bar (L/min) |
|---|---|---|
| K57 (4.0) | 10mm | 47.7 |
| K80 (5.6) | 12mm | 66.9 |
| K115 (8.0) | 15mm | 96.2 |
| K160 (11.2) | 19mm | 134 |
| K200 (14.0) | 22mm | 168 |
| K360 (25.2) | — | 302 |

### Design Criteria by Hazard:
| Hazard | Density (mm/min) | Area (m²) | Coverage/head (m²) | Max Spacing (m) |
|---|---|---|---|---|
| Light | 4.1 | 139 | 21 | 4.6 |
| OH-1 | 6.1 | 139 | 12 | 4.6 |
| OH-2 | 8.2 | 139 | 12 | 4.6 |
| Extra | 12.2 | 232 | 9 | 3.7 |

### Hose Stream Allowances:
| Hazard | Allowance (L/min) | Duration (min) |
|---|---|---|
| Light | 380 | 30 |
| OH-1 | 950 | 60–90 |
| OH-2 | 950 | 60–90 |
| Extra | 950–1900 | 90–120 |

## 8. Electrical Demand Factors (NEC Table 220)

| Load Type | First (kW) | Factor | Remaining | Factor |
|---|---|---|---|---|
| General Lighting (Commercial) | 0–12.5 | 1.00 | >12.5 | 1.00 |
| General Lighting (Dwelling) | 0–3 | 1.00 | >3 | 0.35 |
| Receptacle (Commercial) | 0–10 | 1.00 | >10 | 0.50 |
| Kitchen Equipment | 0–5 items | 1.00 | 6+ | 0.65 |
| HVAC Motor Load | All | 1.25 | — | — |
| Fire Pump | All | 1.00 | — | — |

### Typical Overall Demand Factors:
| Building Type | Demand Factor |
|---|---|
| Office Building | 0.60–0.70 |
| Shopping Mall | 0.70–0.80 |
| Hospital | 0.65–0.75 |
| Hotel | 0.55–0.65 |
| Residential | 0.40–0.60 |
| Industrial | 0.50–0.70 |

### Standard Transformer Sizes (kVA):
```
100, 160, 250, 315, 400, 500, 630, 800, 1000, 1250, 1600, 2000, 2500
```

### Voltage Drop Limits:
| Circuit Type | Max Voltage Drop |
|---|---|
| Branch Circuit | 3% |
| Feeder | 3% |
| Total (Branch + Feeder) | 5% |
