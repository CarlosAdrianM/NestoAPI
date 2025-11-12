#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Script para renombrar NotasEntrega a NotaEntrega en el EDMX
Solo cambia el nombre de la EntityType, NO del EntitySet
"""

import re
from datetime import datetime
import shutil

edmx_path = r"C:\Users\Carlos\source\repos\NestoAPI\NestoAPI\Models\NestoEntities.edmx"
backup_path = f"{edmx_path}.backup_rename_{datetime.now().strftime('%Y%m%d_%H%M%S')}"

print("=" * 60)
print("RENOMBRAR NotasEntrega a NotaEntrega EN EDMX")
print("=" * 60)
print()

# 1. Crear backup
print("[1/3] Creando backup...")
shutil.copy2(edmx_path, backup_path)
print(f"      Backup: {backup_path}")
print()

# 2. Leer archivo
print("[2/3] Leyendo EDMX...")
with open(edmx_path, 'r', encoding='utf-8') as f:
    content = f.read()
print("      EDMX cargado")
print()

# 3. Reemplazos quirúrgicos
print("[3/3] Renombrando NotasEntrega a NotaEntrega...")
cambios = 0

# A. EntityType Name="NotasEntrega" → EntityType Name="NotaEntrega"
# Tanto en Storage Model como en Conceptual Model
old_pattern = r'<EntityType Name="NotasEntrega">'
new_pattern = r'<EntityType Name="NotaEntrega">'
count = content.count(old_pattern)
content = content.replace(old_pattern, new_pattern)
print(f"      [OK] EntityType Name: {count} cambios")
cambios += count

# B. EntityTypeMapping TypeName="NVModel.NotasEntrega" → TypeName="NVModel.NotaEntrega"
old_pattern = r'TypeName="NVModel.NotasEntrega"'
new_pattern = r'TypeName="NVModel.NotaEntrega"'
count = content.count(old_pattern)
content = content.replace(old_pattern, new_pattern)
print(f"      [OK] TypeName: {count} cambios")
cambios += count

# C. EntitySet EntityType="NVModel.NotasEntrega" → EntityType="NVModel.NotaEntrega"
# PERO mantener Name="NotasEntregas" (plural)
old_pattern = r'EntityType="NVModel.NotasEntrega"'
new_pattern = r'EntityType="NVModel.NotaEntrega"'
count = content.count(old_pattern)
content = content.replace(old_pattern, new_pattern)
print(f"      [OK] EntitySet EntityType: {count} cambios")
cambios += count

# D. EntitySet EntityType="Self.NotasEntrega" → EntityType="Self.NotaEntrega"
# (En Storage Model)
old_pattern = r'EntityType="Self.NotasEntrega"'
new_pattern = r'EntityType="Self.NotaEntrega"'
count = content.count(old_pattern)
content = content.replace(old_pattern, new_pattern)
print(f"      [OK] Storage EntitySet EntityType: {count} cambios")
cambios += count

print()
print(f"      Total cambios: {cambios}")
print()

# Verificar que EntitySet Name="NotasEntregas" sigue en plural
if 'EntitySet Name="NotasEntregas"' in content:
    print("      [OK] EntitySet Name='NotasEntregas' mantiene plural (correcto)")
else:
    print("      [WARNING] No se encontro EntitySet Name='NotasEntregas'")

print()

# 4. Guardar
print("Guardando EDMX...")
with open(edmx_path, 'w', encoding='utf-8') as f:
    f.write(content)
print("      EDMX guardado")
print()

print("=" * 60)
print("RENOMBRAMIENTO COMPLETADO")
print("=" * 60)
print()
print("RESULTADO:")
print("  - Clase (EntityType): NotaEntrega (SINGULAR)")
print("  - DbSet (EntitySet): NotasEntregas (PLURAL)")
print()
print("PRÓXIMOS PASOS:")
print("1. Abrir Visual Studio")
print("2. Limpiar solución (Clean Solution)")
print("3. Cerrar y volver a abrir Visual Studio")
print("4. Rebuild Solution")
print()
print(f"Backup: {backup_path}")
print()
