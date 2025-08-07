# LPR381 Solver

A console-based application built in C# (.NET) to solve **Linear Programming (LP)** and **Integer Programming (IP)** problems using various algorithms such as Simplex, Branch & Bound, and Cutting Plane methods.

## 🎯 Project Goal

- Read LP/IP models from a structured input text file.
- Solve them using the selected algorithm.
- Export canonical forms, tableau iterations, and final results to an output file.
- Perform full sensitivity analysis and duality checks.
- Display all intermediate steps via console and output files.

## 🧠 Supported Algorithms

- Primal Simplex Algorithm
- Revised Primal Simplex Algorithm
- Branch & Bound (Simplex & Knapsack)
- Cutting Plane Algorithm
- Sensitivity Analysis & Duality

## 🚀 Getting Started

1. Open the solution in **Visual Studio**.
2. Build the solution to generate `solve.exe`.
3. Use the console interface to load an input file and choose the solving method.

## 📁 Folder Structure


```
LPR381-Solver/
│
├── LPR381-Solver/                # Main project directory
│   ├── Program.cs                # Main menu-driven entry point
│
│   ├── Input/                    # Contains input LP/IP model files (.txt)
│   │   └── sample.txt
│
│   ├── Output/                   # Output files generated from solving
│   │   └── result_sample.txt
│
│   ├── Models/                   # Core data structures
│   │   ├── LinearModel.cs
│   │   ├── Constraint.cs
│   │   └── Variable.cs
│
│   ├── Parsers/                  # File reading and parsing logic
│   │   └── InputFileParser.cs
│
│   ├── Solvers/                  # Algorithm implementations
│   │   ├── PrimalSimplexSolver.cs
│   │   ├── RevisedSimplexSolver.cs
│   │   ├── BranchAndBoundSolver.cs
│   │   ├── KnapsackSolver.cs
│   │   └── CuttingPlaneSolver.cs
│
│   ├── Sensitivity/              # Sensitivity analysis logic
│   │   └── SensitivityAnalyzer.cs
│
│   ├── Menus/                    # Menu and CLI interaction logic
│   │   └── MenuManager.cs
│
│   └── Utils/                    # Helper functions (e.g. math, file writing)
│       └── MathUtils.cs
│
├── README.md                     # Project overview and setup
└── .gitignore                    # Ignore rules for Git
```


## 📦 Build Instructions

# In Visual Studio
Build > Build Solution

# Output binary will be in:
bin/Debug/net6.0/solve.exe
