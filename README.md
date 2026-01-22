# FallGuys Editor

Éditeur de niveaux pour le projet FallGuys.

## 🚀 Installation

### Première installation

1. **Cloner le projet** :
   ```bash
   git clone https://github.com/eraflo/FallGuysEditor.git
   cd FallGuysEditor
   ```

2. **Exécuter le script de setup** :
   - Double-cliquez sur `setup.bat`
   - Ou en ligne de commande :
     ```bash
     ./setup.bat
     ```

3. **Ouvrir le projet dans Unity** (version 2022.3+)

### Avec GitHub Desktop

1. Clonez le projet via GitHub Desktop
2. Ouvrez le dossier dans l'explorateur
3. Double-cliquez sur `setup.bat`
4. Ouvrez le projet dans Unity

## 📦 Packages partagés

Ce projet utilise le package **CommonPackage** (`com.eraflo.common`) comme Git submodule dans `Packages/com.eraflo.common/`.

### Mise à jour du package CommonPackage

Le package est automatiquement mis à jour à chaque `git pull` grâce aux hooks Git configurés par `setup.bat`.

Pour mettre à jour manuellement :
```bash
git submodule update --remote
```

### Modifier le package CommonPackage

Les modifications se font directement dans `Packages/com.eraflo.common/` :

```bash
cd Packages/com.eraflo.common
# Faire vos modifications
git add .
git commit -m "Description des changements"
git push
```

Ensuite, mettez à jour la référence dans le projet principal :
```bash
cd ../..
git add Packages/com.eraflo.common
git commit -m "Update CommonPackage"
git push
```

## 🔧 Structure du projet

```
FallGuysEditor/
├── Assets/              # Assets Unity
├── Packages/
│   ├── com.eraflo.common/  # Submodule CommonPackage
│   └── manifest.json
├── .githooks/           # Hooks Git pour automatisation
├── setup.bat            # Script de configuration initiale
└── README.md
```
