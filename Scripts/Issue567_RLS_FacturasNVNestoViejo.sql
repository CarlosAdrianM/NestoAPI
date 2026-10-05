/*
    NestoAPI#567 (slice 1 de #590): que Nesto viejo NO pueda imprimir (ni ver) las facturas de la serie NV.
    La serie GB sigue como está (decisión de Carlos, 02/10/26).

    Por qué así (traza XE del 05/10/26, factura NV2616038 impresa dos veces, con y sin membrete):
      - No pasa por ningún procedimiento ni función: el programa (APP_NAME() = 'Nesto') lee las tablas a pelo:
        «select * from cabfacturavta where empresa='1' and número='NV…'», sumas de linpedidovta por [nº factura]
        y la FICHA del cliente («select * from clientes … clienteprincipal=1»: de ahí que salga el nombre/NIF de la
        ficha y no el de la cabecera).
      - El informe es F:\NVERP\INFORMES\facturas.rpt (parámetro RutaInformes, el mismo para todos). No se puede
        cambiar ese .rpt por uno que avise, porque la serie GB lo sigue necesitando (opción C descartada).
      - Opción A: seguridad por filas (RLS) en CabFacturaVta, solo para el programa Nesto viejo y la serie NV.
        Sin la cabecera, Nesto viejo no encuentra la factura. No afecta a la API (APP_NAME = EntityFramework /
        NestoAPI-Hangfire), ni a SSMS, ni a Excel, ni a los jobs, ni a la serie GB.
      - Efecto secundario aceptado: en la pantalla de facturas de Nesto viejo tampoco se verán las NV (para eso
        está Nesto).

    Se puede quitar al momento: PASO 4 (STATE = OFF) o PASO 5 (borrarlo todo).

    Ejecutar como sa en NV, POR PASOS (seleccionando cada bloque).
*/

USE NV;
GO

------------------------------------------------------------------------------------------------
-- PASO 1. Crear el esquema y la función del filtro.
--         Mientras se prueba, solo se aplica a los usuarios de la lista @Prueba (Carlos).
------------------------------------------------------------------------------------------------
IF SCHEMA_ID(N'Seguridad') IS NULL
    EXEC (N'CREATE SCHEMA Seguridad AUTHORIZATION dbo;');
GO

CREATE OR ALTER FUNCTION Seguridad.fnFacturaVisibleEnNestoViejo (@Serie char(3))
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS Visible
    WHERE NOT (
              APP_NAME() = N'Nesto'
          AND RTRIM(@Serie) = 'NV'
          -- PRUEBA: solo para Carlos. Para todos, quitar esta línea (PASO 3).
          AND SYSTEM_USER = N'NUEVAVISION\Carlos'
    );
GO

------------------------------------------------------------------------------------------------
-- PASO 2. Activar el filtro en CabFacturaVta.
--         Prueba: con Nesto viejo de Carlos, abrir/imprimir la NV2616038 (no debe salir) y una GB (sí).
--         Y en Nesto (el nuevo) y en SSMS la NV2616038 se tiene que seguir viendo.
------------------------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.security_policies WHERE name = N'FacturasNVFueraDeNestoViejo')
    CREATE SECURITY POLICY Seguridad.FacturasNVFueraDeNestoViejo
        ADD FILTER PREDICATE Seguridad.fnFacturaVisibleEnNestoViejo(Serie) ON dbo.CabFacturaVta
        WITH (STATE = ON, SCHEMABINDING = ON);
GO

-- Comprobación desde SSMS (APP_NAME distinto de 'Nesto'): tiene que devolver la factura.
SELECT Número, Serie, [Nº Cliente] FROM dbo.CabFacturaVta WHERE Empresa = '1' AND Número = 'NV2616038';
GO

------------------------------------------------------------------------------------------------
-- PASO 3. Si la prueba va bien: para todos los usuarios (quitar la condición de SYSTEM_USER).
--         ALTER de la función con la política activa: hay que apagarla un momento.
------------------------------------------------------------------------------------------------
/*
ALTER SECURITY POLICY Seguridad.FacturasNVFueraDeNestoViejo WITH (STATE = OFF);
DROP SECURITY POLICY Seguridad.FacturasNVFueraDeNestoViejo;
GO
CREATE OR ALTER FUNCTION Seguridad.fnFacturaVisibleEnNestoViejo (@Serie char(3))
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS Visible
    WHERE NOT (APP_NAME() = N'Nesto' AND RTRIM(@Serie) = 'NV');
GO
CREATE SECURITY POLICY Seguridad.FacturasNVFueraDeNestoViejo
    ADD FILTER PREDICATE Seguridad.fnFacturaVisibleEnNestoViejo(Serie) ON dbo.CabFacturaVta
    WITH (STATE = ON, SCHEMABINDING = ON);
GO
*/

------------------------------------------------------------------------------------------------
-- PASO 4. Marcha atrás rápida (deja todo creado, sin filtrar).
------------------------------------------------------------------------------------------------
-- ALTER SECURITY POLICY Seguridad.FacturasNVFueraDeNestoViejo WITH (STATE = OFF);

------------------------------------------------------------------------------------------------
-- PASO 5. Quitarlo todo.
------------------------------------------------------------------------------------------------
/*
DROP SECURITY POLICY IF EXISTS Seguridad.FacturasNVFueraDeNestoViejo;
DROP FUNCTION IF EXISTS Seguridad.fnFacturaVisibleEnNestoViejo;
*/
