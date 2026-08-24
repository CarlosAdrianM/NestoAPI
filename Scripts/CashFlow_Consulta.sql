-- =====================================================================================
-- CASH FLOW - consulta corregida (24/08/2026)
--
-- CAMBIOS respecto a la version anterior:
--
--   1. Se ELIMINA de 'Deudores (B13)' la resta del apunte 6513143. Ese apunte es del
--      31/12/2023 y su diario es _ASIENTREG, que el CTE YA excluye: se estaba restando
--      algo que nunca habia entrado. Valia 47,43 del descuadre.
--
--   2. Se anaden las filas 'COLUMNA 31/12/2025 -> ...', que calculan los saldos de cierre
--      de 2025 a partir del ASIENTO DE APERTURA (que es, literalmente, el balance a
--      31/12/2025). Antes eran constantes tecleadas en el Excel y se habian quedado
--      desfasadas al regenerarse el asiento de apertura de la empresa 1 el 22/07/2026:
--          Capital circulante: 354.253,60 -> 371.133,73   (+16.880,13)
--          Confirming:         245.726,18 -> 263.726,18   (+18.000,00)
--      Pegando tambien estas filas en la columna C, el cuadre deja de depender de unas
--      constantes que envejecen sin avisar.
-- =====================================================================================
declare @desde as datetime = '01/01/26'
declare @hasta as datetime = '31/07/26'

;with cte as (
    select [Nº Cuenta] Cuenta, (Debe - Haber) Importe
    from Contabilidad
    where Fecha >= @desde and Fecha < DATEADD(dd, 1, @hasta)
    and not (diario = '_asientcie' and Fecha > @hasta) and diario <> '_asientreg'
),
-- El asiento de apertura ES el balance a 31/12/2025.
ape as (
    select [Nº Cuenta] Cuenta, (Debe - Haber) Importe
    from Contabilidad
    where Fecha >= '01/01/2026' and Fecha < '02/01/2026' and Diario = '_ASIENTCIE'
)

------------------------------------------------------------------ COLUMNA B (a @hasta)
select 'BAI (B3)' as Concepto, -SUM(Importe) as Valor
from cte
where (Cuenta like '6%' or Cuenta like '7%') and Cuenta not like '630%'
UNION ALL
select 'Amortizaciones (B4)', SUM(Importe) from cte where Cuenta like '68%'
UNION ALL
select 'Impuesto s/beneficios (B5)', (SELECT SUM(-Importe) FROM cte WHERE Cuenta Like '630%')
UNION ALL
select 'Ajustes periodificacion (B6)',
    (SELECT SUM(-Importe) FROM cte WHERE Cuenta Like '480%' OR Cuenta Like '580%' OR Cuenta Like '482%')
UNION ALL
select 'Variac. existencias (B12)',
    (SELECT SUM(Importe) FROM cte WHERE Cuenta Between '30' and '36999999' or Cuenta Like '407%')
  - (SELECT ISNULL(SUM(Importe), 0) FROM cte WHERE Cuenta Like '39%')
UNION ALL
select 'Deudores (B13)',
    (SELECT SUM(Importe) FROM cte WHERE (Cuenta Between '430' and '43399999')
        OR Cuenta Like '435%' OR Cuenta Like '44%' OR Cuenta Like '460%' OR Cuenta Like '470%'
        OR Cuenta Like '471%' OR Cuenta Like '472%' OR Cuenta Like '473%' OR Cuenta Like '474%'
        OR Cuenta Like '490%' OR Cuenta Like '544%'
        OR Cuenta Like '551%' OR Cuenta Like '552%' OR Cuenta Like '553%'  -- duplicadas en acreedores (hoy sin movimiento)
        OR Cuenta Like '478%' OR Cuenta Like '550%')
  - (SELECT ISNULL(SUM(Importe), 0) FROM cte WHERE Cuenta Like '436%' OR Cuenta Like '493%' OR Cuenta Like '494%')
  -- (2) FUERA la resta del apunte 6513143
UNION ALL
select 'Acreedores (restar en B14)',
    (SELECT SUM(Importe) FROM cte WHERE (Cuenta Between '400' and '40399999')
        OR Cuenta Like '41%' OR Cuenta Like '437%' OR Cuenta Like '438%' OR Cuenta Like '465%'
        OR Cuenta Like '475%' OR Cuenta Like '476%' OR Cuenta Like '477%' OR Cuenta Like '479%'
        OR Cuenta Like '485%' OR Cuenta Like '499%' OR Cuenta Like '50%' OR Cuenta Like '51%'
        OR (Cuenta Like '52[2-6]%')
        OR Cuenta Like '551%' OR Cuenta Like '552%' OR Cuenta Like '553%' OR Cuenta Like '555%'
        OR Cuenta Like '556%' OR Cuenta Like '560%' OR Cuenta Like '561%' OR Cuenta Like '585%')
  - (SELECT ISNULL(SUM(Importe), 0) FROM cte WHERE Cuenta Like '406%' OR Cuenta Like '528%')
UNION ALL
select 'Inmov. Intang. (B23)',
    (SELECT ISNULL(SUM(Importe),0) FROM cte WHERE Cuenta Like '21%' OR Cuenta Like '281%')
  - (SELECT ISNULL(SUM(Importe), 0) FROM cte WHERE Cuenta Like '291%')
UNION ALL
select 'Inmov. Material (B24)',
    (SELECT SUM(Importe) FROM cte WHERE Cuenta Like '22%' OR Cuenta Like '23%' OR Cuenta Like '282%')
  - (SELECT ISNULL(SUM(Importe), 0) FROM cte WHERE Cuenta Like '292%')
UNION ALL
select 'Otras inv. a LP (B30)',
    (SELECT SUM(Importe) FROM cte WHERE Cuenta Like '53%' OR (Cuenta Between '540' and '54399999')
        OR (Cuenta Between '545' and '54899999') OR Cuenta Like '565%' OR Cuenta Like '566%' OR Cuenta Like '260%')
  - (SELECT ISNULL(SUM(Importe), 0) FROM cte WHERE Cuenta Like '549%' OR Cuenta Like '59%')
UNION ALL
select 'Bancos (B37)', (SELECT SUM(Importe) FROM cte WHERE Cuenta Like '57%')
UNION ALL
select 'Variacion deudas LP (B39)',
    (SELECT SUM(-Importe) FROM cte WHERE (Cuenta Between '15' and '18999999')
        OR Cuenta Like '248%' OR Cuenta Like '249%' OR Cuenta Like '259%' OR Cuenta Like '528%')
UNION ALL
select 'Variacion deudas CP (B40)',
    (SELECT SUM(-Importe) FROM cte WHERE Cuenta Like '5105%'
        OR (Cuenta Like '520%' and Cuenta != '52000013' and Cuenta != '52000014')
        OR Cuenta Like '52100015' OR Cuenta Like '52100016' OR Cuenta Like '527%')
UNION ALL
select 'Confirming y linea riesgo (B42)', SUM(-Importe) from cte
    where Cuenta = '52000013' or Cuenta = '52000014' OR Cuenta Like '52100017'
UNION ALL
select 'Dividendo a socios (D55)',
    (select SUM(-Debe+Haber) from Contabilidad
     where fecha between @desde and @hasta and Empresa = '3' and [Nº Cuenta] like '1%'
     and Diario != '_ASIENTCIE')

------------------------------------- COLUMNA C (31/12/2025), desde el asiento de apertura
UNION ALL
select 'COLUMNA 31/12/2025 -> Existencias (C12)',
    (SELECT SUM(Importe) FROM ape WHERE Cuenta Between '30' and '36999999' or Cuenta Like '407%')
  - (SELECT ISNULL(SUM(Importe), 0) FROM ape WHERE Cuenta Like '39%')
UNION ALL
select 'COLUMNA 31/12/2025 -> Deudores (C13)',
    (SELECT SUM(Importe) FROM ape WHERE (Cuenta Between '430' and '43399999')
        OR Cuenta Like '435%' OR Cuenta Like '44%' OR Cuenta Like '460%' OR Cuenta Like '470%'
        OR Cuenta Like '471%' OR Cuenta Like '472%' OR Cuenta Like '473%' OR Cuenta Like '474%'
        OR Cuenta Like '490%' OR Cuenta Like '544%' OR Cuenta Like '551%' OR Cuenta Like '552%'
        OR Cuenta Like '553%' OR Cuenta Like '478%' OR Cuenta Like '550%')
  - (SELECT ISNULL(SUM(Importe), 0) FROM ape WHERE Cuenta Like '436%' OR Cuenta Like '493%' OR Cuenta Like '494%')
UNION ALL
select 'COLUMNA 31/12/2025 -> Acreedores (C14)',
    (SELECT SUM(Importe) FROM ape WHERE (Cuenta Between '400' and '40399999')
        OR Cuenta Like '41%' OR Cuenta Like '437%' OR Cuenta Like '438%' OR Cuenta Like '465%'
        OR Cuenta Like '475%' OR Cuenta Like '476%' OR Cuenta Like '477%' OR Cuenta Like '479%'
        OR Cuenta Like '485%' OR Cuenta Like '499%' OR Cuenta Like '50%' OR Cuenta Like '51%'
        OR (Cuenta Like '52[2-6]%')
        OR Cuenta Like '551%' OR Cuenta Like '552%' OR Cuenta Like '553%' OR Cuenta Like '555%'
        OR Cuenta Like '556%' OR Cuenta Like '560%' OR Cuenta Like '561%' OR Cuenta Like '585%')
  - (SELECT ISNULL(SUM(Importe), 0) FROM ape WHERE Cuenta Like '406%' OR Cuenta Like '528%')
UNION ALL
select 'COLUMNA 31/12/2025 -> Inmov. Intang. (C23)',
    (SELECT ISNULL(SUM(Importe),0) FROM ape WHERE Cuenta Like '21%' OR Cuenta Like '281%')
  - (SELECT ISNULL(SUM(Importe), 0) FROM ape WHERE Cuenta Like '291%')
UNION ALL
select 'COLUMNA 31/12/2025 -> Inmov. Material (C24)',
    (SELECT SUM(Importe) FROM ape WHERE Cuenta Like '22%' OR Cuenta Like '23%' OR Cuenta Like '282%')
  - (SELECT ISNULL(SUM(Importe), 0) FROM ape WHERE Cuenta Like '292%')
UNION ALL
select 'COLUMNA 31/12/2025 -> Otras inv. a LP (C30)',
    (SELECT SUM(Importe) FROM ape WHERE Cuenta Like '53%' OR (Cuenta Between '540' and '54399999')
        OR (Cuenta Between '545' and '54899999') OR Cuenta Like '565%' OR Cuenta Like '566%' OR Cuenta Like '260%')
  - (SELECT ISNULL(SUM(Importe), 0) FROM ape WHERE Cuenta Like '549%' OR Cuenta Like '59%')
UNION ALL
select 'COLUMNA 31/12/2025 -> Bancos (C37)', (SELECT SUM(Importe) FROM ape WHERE Cuenta Like '57%')
UNION ALL
select 'COLUMNA 31/12/2025 -> Deudas LP (C39)',
    (SELECT SUM(-Importe) FROM ape WHERE (Cuenta Between '15' and '18999999')
        OR Cuenta Like '248%' OR Cuenta Like '249%' OR Cuenta Like '259%' OR Cuenta Like '528%')
UNION ALL
select 'COLUMNA 31/12/2025 -> Deudas CP (C40)',
    (SELECT ISNULL(SUM(-Importe),0) FROM ape WHERE Cuenta Like '5105%'
        OR (Cuenta Like '520%' and Cuenta != '52000013' and Cuenta != '52000014')
        OR Cuenta Like '52100015' OR Cuenta Like '52100016' OR Cuenta Like '527%')
UNION ALL
select 'COLUMNA 31/12/2025 -> Confirming (C42)',
    (SELECT SUM(-Importe) FROM ape WHERE Cuenta = '52000013' or Cuenta = '52000014' OR Cuenta Like '52100017')
