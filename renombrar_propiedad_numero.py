#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Script para renombrar la propiedad NotaEntrega a Numero en el EntityType NotaEntrega
Esto evita el conflicto: "Los nombres de los miembros no pueden ser iguales que su tipo envolvente"
"""

import re
from datetime import datetime
import shutil

edmx_path = r"C:\Users\Carlos\source\repos\NestoAPI\NestoAPI\Models\NestoEntities.edmx"
backup_path = f"{edmx_path}.backup_numero_{datetime.now().strftime('%Y%m%d_%H%M%S')}"

print("=" * 70)
print("RENOMBRAR PROPIEDAD NotaEntrega A Numero")
print("=" * 70)
print()

# 1. Crear backup
print("[1/3] Creando backup...")
shutil.copy2(edmx_path, backup_path)
print(f"      Backup: {backup_path}")
print()

# 2. Leer archivo
print("[2/3] Leyendo EDMX...")
with open(edmx_path, 'r', encoding='utf-8') as f:
    lines = f.readlines()
print(f"      EDMX cargado ({len(lines)} lineas)")
print()

# 3. Buscar y reemplazar en el EntityType NotaEntrega del Conceptual Model
print("[3/3] Renombrando propiedad en EntityType NotaEntrega...")

# Variables de control
dentro_de_entitytype_notaentrega = False
dentro_de_key = False
cambios = 0

for i in range(len(lines)):
    line = lines[i]

    # Detectar inicio de EntityType NotaEntrega (Conceptual Model - tiene Type="Int32")
    if '<EntityType Name="NotaEntrega">' in line and i > 10000:  # Solo en Conceptual Model (línea ~12340)
        dentro_de_entitytype_notaentrega = True
        print(f"      Encontrado EntityType NotaEntrega en linea {i+1}")
        continue

    # Si estamos dentro del EntityType NotaEntrega correcto
    if dentro_de_entitytype_notaentrega:
        # Detectar Key
        if '<Key>' in line:
            dentro_de_key = True
            continue

        if '</Key>' in line:
            dentro_de_key = False
            continue

        # Cambiar PropertyRef en Key: NotaEntrega -> Numero
        if dentro_de_key and 'PropertyRef Name="NotaEntrega"' in line:
            lines[i] = line.replace('PropertyRef Name="NotaEntrega"', 'PropertyRef Name="Numero"')
            print(f"      [OK] Linea {i+1}: PropertyRef en Key")
            cambios += 1
            continue

        # Cambiar Property definition: Name="NotaEntrega" -> Name="Numero"
        if 'Property Name="NotaEntrega" Type="Int32"' in line:
            lines[i] = line.replace('Property Name="NotaEntrega"', 'Property Name="Numero"')
            print(f"      [OK] Linea {i+1}: Property definition")
            cambios += 1
            continue

        # Detectar fin de EntityType
        if '</EntityType>' in line:
            dentro_de_entitytype_notaentrega = False
            print(f"      Fin de EntityType NotaEntrega en linea {i+1}")
            continue

# 4. Cambiar en el Mapping: ScalarProperty Name="NotaEntrega" -> Name="Numero"
# PERO mantener ColumnName="NotaEntrega"
print()
print("Renombrando propiedad en Mapping...")
dentro_de_mapping_notasentregas = False

for i in range(len(lines)):
    line = lines[i]

    # Detectar inicio del EntitySetMapping para NotasEntregas
    if '<EntitySetMapping Name="NotasEntregas">' in line:
        dentro_de_mapping_notasentregas = True
        print(f"      Encontrado EntitySetMapping NotasEntregas en linea {i+1}")
        continue

    if dentro_de_mapping_notasentregas:
        # Cambiar ScalarProperty Name="NotaEntrega" -> Name="Numero"
        # PERO mantener ColumnName="NotaEntrega"
        if 'ScalarProperty Name="NotaEntrega" ColumnName="NotaEntrega"' in line:
            lines[i] = line.replace(
                'ScalarProperty Name="NotaEntrega" ColumnName="NotaEntrega"',
                'ScalarProperty Name="Numero" ColumnName="NotaEntrega"'
            )
            print(f"      [OK] Linea {i+1}: ScalarProperty en Mapping")
            cambios += 1
            continue

        # Detectar fin del EntitySetMapping
        if '</EntitySetMapping>' in line:
            dentro_de_mapping_notasentregas = False
            print(f"      Fin de EntitySetMapping en linea {i+1}")
            break

print()
print(f"      Total cambios: {cambios}")
print()

# 5. Guardar
print("Guardando EDMX...")
with open(edmx_path, 'w', encoding='utf-8') as f:
    f.writelines(lines)
print("      EDMX guardado")
print()

print("=" * 70)
print("RENOMBRAMIENTO COMPLETADO")
print("=" * 70)
print()
print("RESULTADO:")
print("  - Clase: NotaEntrega")
print("  - Propiedad en C#: Numero (no NotaEntrega)")
print("  - Columna en SQL: NotaEntrega (correcto)")
print()
print("CODIGO:")
print("  var nota = new NotaEntrega { Numero = 123, ... };")
print()
print("PROXIMOS PASOS:")
print("1. Abrir Visual Studio")
print("2. Rebuild Solution")
print()
print(f"Backup: {backup_path}")
print()
