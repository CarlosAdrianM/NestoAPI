#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Script para limpiar NotasEntrega del EDMX
Elimina todas las definiciones de NotasEntrega para poder agregarla de nuevo
"""

import re
from datetime import datetime
import shutil

edmx_path = r"C:\Users\Carlos\source\repos\NestoAPI\NestoAPI\Models\NestoEntities.edmx"
backup_path = f"{edmx_path}.backup_{datetime.now().strftime('%Y%m%d_%H%M%S')}"

print("=" * 50)
print("LIMPIEZA DE NOTASENTREGA DEL EDMX")
print("=" * 50)
print()

# 1. Crear backup
print("[1/4] Creando backup...")
shutil.copy2(edmx_path, backup_path)
print(f"      Backup: {backup_path}")
print()

# 2. Leer archivo
print("[2/4] Leyendo EDMX...")
with open(edmx_path, 'r', encoding='utf-8') as f:
    content = f.read()
print("      EDMX cargado")
print()

# 3. Eliminar secciones
print("[3/4] Eliminando secciones de NotasEntrega...")
eliminados = 0

# A. Eliminar EntityType "NotasEntrega" del Storage Model (línea 1180)
pattern_storage = r'<EntityType Name="NotasEntrega">.*?</EntityType>\s*'
matches = list(re.finditer(pattern_storage, content, re.DOTALL))
for match in matches:
    # Solo eliminar si está en el Storage Model (tiene Type="int" o Type="datetime", no Type="Int32")
    if 'Type="int"' in match.group() or 'Type="datetime"' in match.group():
        print(f"      Eliminando EntityType NotasEntrega (Storage Model)")
        content = content.replace(match.group(), '')
        eliminados += 1
        break

# B. Eliminar EntityType "NotasEntrega" del Conceptual Model (línea 12260)
pattern_conceptual = r'<EntityType Name="NotasEntrega">.*?</EntityType>\s*'
matches = list(re.finditer(pattern_conceptual, content, re.DOTALL))
for match in matches:
    # Solo eliminar si está en el Conceptual Model (tiene Type="Int32" o Type="DateTime", no Type="int")
    if 'Type="Int32"' in match.group() or 'Type="DateTime"' in match.group():
        print(f"      Eliminando EntityType NotasEntrega (Conceptual Model)")
        content = content.replace(match.group(), '')
        eliminados += 1
        break

# C. Eliminar EntitySet "NotasEntrega" del Storage EntityContainer
pattern_entityset_storage = r'<EntitySet Name="NotasEntrega".*?/>.*?\n'
content = re.sub(pattern_entityset_storage, '', content)
eliminados += 1
print(f"      Eliminando EntitySet NotasEntrega (Storage)")

# D. Eliminar EntitySet "NotasEntregas" del Conceptual EntityContainer (línea 6981)
pattern_entityset_conceptual = r'<EntitySet Name="NotasEntregas" EntityType="NVModel\.NotasEntrega" />.*?\n'
content = re.sub(pattern_entityset_conceptual, '', content)
eliminados += 1
print(f"      Eliminando EntitySet NotasEntregas (Conceptual)")

# E. Eliminar EntitySetMapping (línea 14193)
pattern_mapping = r'<EntitySetMapping Name="NotasEntregas">.*?</EntitySetMapping>\s*'
content = re.sub(pattern_mapping, '', content, flags=re.DOTALL)
eliminados += 1
print(f"      Eliminando EntitySetMapping NotasEntregas")

print()
print(f"      Total secciones procesadas: {eliminados}")
print()

# 4. Guardar
print("[4/4] Guardando EDMX limpio...")
with open(edmx_path, 'w', encoding='utf-8') as f:
    f.write(content)
print("      EDMX guardado")
print()

print("=" * 50)
print("LIMPIEZA COMPLETADA")
print("=" * 50)
print()
print("PROXIMOS PASOS:")
print("1. Abrir Visual Studio")
print("2. Abrir NestoEntities.edmx")
print("3. Update Model from Database")
print("4. Agregar tabla NotasEntrega")
print("5. Renombrar entidad a 'NotaEntrega' (singular)")
print()
print(f"Backup: {backup_path}")
print()
