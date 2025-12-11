-- Análisis de descuadres para pedidos: 904496, 904711, 904875, 904880, 904887
-- Fecha: 02/12/2025

-- ============================================================================
-- PEDIDO 904496 (Total: 37,30 €)
-- ============================================================================
-- Líneas:
-- 1) Bruto=10.1217, BI=10.1217, IVA=2.1256, Total=12.2473 (sin dto)
-- 2) Bruto=17.204,  BI=17.204,  IVA=3.6128, Total=20.8168 (sin dto)
-- 3) Bruto=3.5041,  BI=3.5041,  IVA=0.7359, Total=4.24    (cuenta contable, tipolinea=2)

-- Vista vstContabilizarFacturaVta devolvería (agrupado por %IVA, %RE):
-- SUM(BaseImponible) = 10.1217 + 17.204 + 3.5041 = 30.8298
-- SUM(Total) = 12.2473 + 20.8168 + 4.24 = 37.3041

-- SP calcula @TotalCliente:
-- @BaseIVA = 30.8298
-- @ImporteIVA = ROUND(30.8298 * 21 / 100, 2) = ROUND(6.474258, 2) = 6.47
-- @TotalCliente = ROUND(30.8298, 2) + ROUND(6.47, 2) = 30.83 + 6.47 = 37.30

-- SP calcula @TotalClienteCuadre:
-- SUM(Total) agrupado = 37.3041
-- @TotalClienteCuadre = ROUND(37.3041, 2) = 37.30

-- Diferencia = |37.30 - 37.30| = 0.00 ✓ NO debería dar descuadre

SELECT '904496' as Pedido,
       30.8298 as SumBI,
       ROUND(30.8298 * 21 / 100.0, 2) as IVA_Calculado,
       ROUND(30.8298, 2) + ROUND(30.8298 * 21 / 100.0, 2) as TotalCliente,
       37.3041 as SumTotal,
       ROUND(37.3041, 2) as TotalClienteCuadre,
       ABS(ROUND(30.8298, 2) + ROUND(30.8298 * 21 / 100.0, 2) - ROUND(37.3041, 2)) as Diferencia

-- ============================================================================
-- PEDIDO 904711 (Total: 298,81 €)
-- ============================================================================
-- Líneas con Total > 0 (excluyendo muestras con Total=0):
-- 38119: Bruto=19.5509, BI=19.5509, Total=23.6566
-- 42511: Bruto=35.055,  BI=35.055,  Total=42.4166
-- 38179: Bruto=45.405,  BI=45.405,  Total=54.9401
-- 43541: Bruto=22.781,  BI=22.781,  Total=27.565
-- 42510: Bruto=47.70,   BI=47.70,   Total=57.717
-- 43542: Bruto=15.7682, BI=15.7682, Total=19.0795
-- 44713: Bruto=67.4325, BI=60.6925, Total=73.4379 (tiene 10% dto)
-- Muestras (Total=0): 38271, 38272, 36523

-- SUM(BaseImponible) = 19.5509 + 35.055 + 45.405 + 22.781 + 47.70 + 15.7682 + 60.6925 + 0 + 0 + 0 = 246.9526
-- SUM(Total) = 23.6566 + 42.4166 + 54.9401 + 27.565 + 57.717 + 19.0795 + 73.4379 + 0 + 0 + 0 = 298.8127

-- SP calcula @TotalCliente:
-- @BaseIVA = 246.9526
-- @ImporteIVA = ROUND(246.9526 * 21 / 100, 2) = ROUND(51.860046, 2) = 51.86
-- @TotalCliente = ROUND(246.9526, 2) + 51.86 = 246.95 + 51.86 = 298.81

-- SP calcula @TotalClienteCuadre:
-- @TotalClienteCuadre = ROUND(298.8127, 2) = 298.81

-- Diferencia = |298.81 - 298.81| = 0.00 ✓ NO debería dar descuadre

SELECT '904711' as Pedido,
       246.9526 as SumBI,
       ROUND(246.9526 * 21 / 100.0, 2) as IVA_Calculado,
       ROUND(246.9526, 2) + ROUND(246.9526 * 21 / 100.0, 2) as TotalCliente,
       298.8127 as SumTotal,
       ROUND(298.8127, 2) as TotalClienteCuadre,
       ABS(ROUND(246.9526, 2) + ROUND(246.9526 * 21 / 100.0, 2) - ROUND(298.8127, 2)) as Diferencia

-- ============================================================================
-- PEDIDO 904875 (Total: 90,27 €)
-- ============================================================================
-- Líneas:
-- 37539: Bruto=28.359,  BI=28.359,  Total=34.3144 (sin dto)
-- 39310: Bruto=27.965,  BI=27.965,  Total=33.8377 (sin dto)
-- 20378: Bruto=16.215,  BI=16.215,  Total=19.6202 (sin dto)
-- 39189: Bruto=2.065,   BI=2.065,   Total=2.4987  (sin dto)
-- 41558: Bruto=0.80,    BI=0,       Total=0       (muestra 100% dto)

-- SUM(BaseImponible) = 28.359 + 27.965 + 16.215 + 2.065 + 0 = 74.604
-- SUM(Total) = 34.3144 + 33.8377 + 19.6202 + 2.4987 + 0 = 90.271

-- SP calcula @TotalCliente:
-- @BaseIVA = 74.604
-- @ImporteIVA = ROUND(74.604 * 21 / 100, 2) = ROUND(15.66684, 2) = 15.67
-- @TotalCliente = ROUND(74.604, 2) + 15.67 = 74.60 + 15.67 = 90.27

-- SP calcula @TotalClienteCuadre:
-- @TotalClienteCuadre = ROUND(90.271, 2) = 90.27

-- Diferencia = |90.27 - 90.27| = 0.00 ✓ NO debería dar descuadre

SELECT '904875' as Pedido,
       74.604 as SumBI,
       ROUND(74.604 * 21 / 100.0, 2) as IVA_Calculado,
       ROUND(74.604, 2) + ROUND(74.604 * 21 / 100.0, 2) as TotalCliente,
       90.271 as SumTotal,
       ROUND(90.271, 2) as TotalClienteCuadre,
       ABS(ROUND(74.604, 2) + ROUND(74.604 * 21 / 100.0, 2) - ROUND(90.271, 2)) as Diferencia

-- ============================================================================
-- PEDIDO 904880 (Total: 86,44 €) - ¡ESTE TIENE UN PRECIO CON 4 DECIMALES!
-- ============================================================================
-- Líneas:
-- 44730: Bruto=22.95,   BI=22.95,   IVA=4.8195,  Total=27.7695 (sin dto)
-- 35894: Bruto=23.065,  BI=23.065,  IVA=4.8437,  Total=27.9087 (sin dto)
-- 23960: Bruto=4.4998,  BI=4.50,    IVA=0.945,   Total=5.445   (sin dto) ← ¡PROBLEMA! Bruto != BI
-- 44639: Bruto=20.925,  BI=20.925,  IVA=4.3943,  Total=25.3193 (sin dto)
-- 41980: Bruto=1.71,    BI=0,       Total=0                     (muestra 100% dto)
-- 43832: Bruto=1.50,    BI=0,       Total=0                     (muestra 100% dto)

-- ¡OJO! Línea 23960: Bruto=4.4998 pero BI=4.50
-- Esto significa que guardamos BI redondeada pero Bruto con 4 decimales

-- SUM(BaseImponible) = 22.95 + 23.065 + 4.50 + 20.925 + 0 + 0 = 71.44
-- SUM(Total) = 27.7695 + 27.9087 + 5.445 + 25.3193 + 0 + 0 = 86.4425

-- Pero la vista hace SUM(ROUND(Bruto, 2)) para ImporteBruto:
-- SUM(ROUND(Bruto,2)) = ROUND(22.95,2) + ROUND(23.065,2) + ROUND(4.4998,2) + ROUND(20.925,2) + ROUND(1.71,2) + ROUND(1.50,2)
--                     = 22.95 + 23.07 + 4.50 + 20.93 + 1.71 + 1.50 = 74.66

-- SP calcula @TotalCliente:
-- @BaseIVA = 71.44
-- @ImporteIVA = ROUND(71.44 * 21 / 100, 2) = ROUND(15.0024, 2) = 15.00
-- @TotalCliente = ROUND(71.44, 2) + 15.00 = 71.44 + 15.00 = 86.44

-- SP calcula @TotalClienteCuadre:
-- @TotalClienteCuadre = ROUND(86.4425, 2) = 86.44

-- Diferencia = |86.44 - 86.44| = 0.00

-- PERO ESPERA... El Total de la línea 23960 es 5.445
-- Si BI=4.50 y IVA=21%, entonces IVA debería ser 4.50 * 0.21 = 0.945
-- Total = 4.50 + 0.945 = 5.445 ✓ Esto cuadra

-- Verifiquemos: ¿El Total guardado coincide con BI + BI*IVA?
-- Línea 23960: BI=4.50, IVA guardado=0.945, Total=5.445
-- Cálculo: 4.50 + 0.945 = 5.445 ✓

-- Entonces ¿por qué da error CK_LinPedidoVta_5?
-- Porque el auto-fix intentó MODIFICAR Bruto, no porque haya descuadre contable

SELECT '904880' as Pedido,
       71.44 as SumBI,
       ROUND(71.44 * 21 / 100.0, 2) as IVA_Calculado,
       ROUND(71.44, 2) + ROUND(71.44 * 21 / 100.0, 2) as TotalCliente,
       86.4425 as SumTotal,
       ROUND(86.4425, 2) as TotalClienteCuadre,
       ABS(ROUND(71.44, 2) + ROUND(71.44 * 21 / 100.0, 2) - ROUND(86.4425, 2)) as Diferencia

-- ============================================================================
-- PEDIDO 904887 (Total: 138,72 €)
-- ============================================================================
-- Líneas:
-- 44713: Bruto=67.4325, BI=57.3225, IVA=12.0377, Total=69.3602 (15% dto: ImporteDto=10.11)
-- 44711: Bruto=67.4325, BI=57.3225, IVA=12.0377, Total=69.3602 (15% dto: ImporteDto=10.11)
-- 36523: Bruto=1.80,    BI=0,       Total=0                     (muestra 100% dto)

-- Verificar línea 44713:
-- Bruto = 67.4325
-- Dto = 15% → ImporteDto = ROUND(67.4325 * 0.15, 2) = ROUND(10.114875, 2) = 10.11
-- BI = Bruto - ImporteDto = 67.4325 - 10.11 = 57.3225 ✓
-- IVA = 57.3225 * 0.21 = 12.037725 → guardado como 12.0377 ✓
-- Total = 57.3225 + 12.0377 = 69.3602 ✓

-- SUM(BaseImponible) = 57.3225 + 57.3225 + 0 = 114.645
-- SUM(Total) = 69.3602 + 69.3602 + 0 = 138.7204

-- SP calcula @TotalCliente:
-- @BaseIVA = 114.645
-- @ImporteIVA = ROUND(114.645 * 21 / 100, 2) = ROUND(24.07545, 2) = 24.08
-- @TotalCliente = ROUND(114.645, 2) + 24.08 = 114.65 + 24.08 = 138.73

-- SP calcula @TotalClienteCuadre:
-- @TotalClienteCuadre = ROUND(138.7204, 2) = 138.72

-- ¡DIFERENCIA! = |138.73 - 138.72| = 0.01 ← Esto está dentro de tolerancia (< 0.02)

-- Pero veamos más detalle...
-- El problema puede estar en cómo se redondea la suma de BI vs la suma de Total

SELECT '904887' as Pedido,
       114.645 as SumBI,
       ROUND(114.645 * 21 / 100.0, 2) as IVA_Calculado,
       ROUND(114.645, 2) + ROUND(114.645 * 21 / 100.0, 2) as TotalCliente,
       138.7204 as SumTotal,
       ROUND(138.7204, 2) as TotalClienteCuadre,
       ABS(ROUND(114.645, 2) + ROUND(114.645 * 21 / 100.0, 2) - ROUND(138.7204, 2)) as Diferencia

-- ============================================================================
-- CONCLUSIÓN
-- ============================================================================
-- Ninguno de estos pedidos debería dar descuadre contable (diferencia < 0.02)
--
-- Los errores CK_LinPedidoVta_5 fueron causados por el auto-fix que intentaba
-- modificar el campo Bruto, NO por un descuadre real en el asiento.
--
-- El auto-fix actual NO debería tocar Bruto, solo recalcular:
-- - ImporteDto
-- - BaseImponible
-- - ImporteIVA
-- - ImporteRE
-- - Total
-- ============================================================================
