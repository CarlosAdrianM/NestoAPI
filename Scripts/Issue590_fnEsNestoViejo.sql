/*
    NestoAPI#590 (capar Nesto viejo por slices), 09/11/26. PIEZA COMÚN de los bloqueos de Nesto viejo. Ejecutar como sa en NV,
    ANTES que cualquiera de estos (que la necesitan):
        Issue590_QuitarPicking_BloqueoNestoViejo.sql
        Issue590_Inventario_BloqueoNestoViejo.sql
        Issue590_DireccionesClientes_BloqueoNestoViejo.sql
    Se puede lanzar entero y repetir sin miedo (idempotente). No cambia ningún dato ni ningún permiso.

    INTERRUPTOR GENERAL (apaga de golpe TODOS los bloqueos de Nesto viejo, en una línea):
        CREATE OR ALTER FUNCTION dbo.fnEsNestoViejo() RETURNS bit WITH SCHEMABINDING AS BEGIN RETURN 0; END
    (Para volver a encenderlos, lanzar este script otra vez.) Cada bloqueo tiene además su interruptor propio (su fila «_Modo»).

    Qué crea:
      1) dbo.fnEsNestoViejo(): 1 si la conexión es de Nesto viejo. Cómo se reconoce (09/11/26, sys.dm_exec_sessions): Nesto viejo
         entra por ODBC con APP_NAME() = 'Nesto' y el login de Windows de cada usuario (NUEVAVISION\Alfredo…). Nada más usa ese
         nombre: la API entra como «EntityFramework», «NestoAPI-Hangfire», «NestoAPI-PreciosMediosIncremental» o «.Net SqlClient
         Data Provider» (RDS2016$); Nesto nuevo no se conecta a la BD (va por la API); SSMS, Agente, jobs y Excel tienen el suyo.
         OJO: Crystal (facturas.rpt) entra también como «Nesto» (traza #567 del 05/10/26). La sesión «DataProj» de Andre (¿Ariadna
         vieja?) NO cuenta como Nesto viejo.
      2) dbo.fnNestoViejoPermitido(@Operacion): 1 si el usuario tiene excepción para esa operación. Se lee de ParametrosUsuario
         (Empresa '1', Clave 'NestoViejo_<Operacion>'): la fila del usuario (sin dominio, como guarda Nesto viejo sus parámetros:
         'Alfredo', a veces con espacios al final) y, si no tiene, la de «(defecto)». Valor '1' = permitido; lo demás = no.
         Cada script de bloqueo da de alta su fila «(defecto)» = '0'.
         Para dar una excepción (ejemplo, Alfredo en direcciones):
            INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
            VALUES ('1', 'NestoViejo_Direcciones', 'Alfredo', '1', 'NestoAPI#590', GETDATE());
      3) dbo.fnNestoViejoModo(@Operacion): 'Bloqueo' o 'Sombra', de la fila «(defecto)» Clave 'NestoViejo_<Operacion>_Modo'.
         Sombra = deja hacer y lo apunta en dbo.NestoViejoSombra. Sin fila o con otro valor = 'Sombra' (lo seguro).
      4) dbo.NestoViejoSombra: el rastro (Fecha, Usuario, Operacion, Resultado, Tabla, Clave, Detalle, Equipo). Resultado =
         'Sombra' (se dejó hacer, modo sombra), 'Permitido' (se dejó hacer por excepción del usuario) o 'Bloqueado'.
         La escriben los desencadenadores y procedimientos de dbo (encadenamiento de propiedad): no hace falta GRANT a nadie.

    Rendimiento: fnEsNestoViejo no lee ninguna tabla (solo APP_NAME()); los bloqueos la llaman una vez por sentencia y salen en
    la primera línea cuando no es Nesto viejo, que es el 99 % del tráfico (la API). Las otras dos solo se llaman si es Nesto viejo.

    Comprobado en producción el 09/11/26 (login de solo lectura): no existe ningún objeto con «NestoViejo» en el nombre;
    ParametrosUsuario = (Empresa char(3), Clave char(50), Usuario varchar(30), Valor char(162), Usuario2 varchar(30) NOT NULL,
    [Fecha Modificación] datetime NOT NULL), PK (Empresa, Clave, Usuario), sin desencadenadores.
*/
USE NV;
GO
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- ANTES
SELECT 'ANTES' Momento, name, type_desc, create_date, modify_date
FROM sys.objects
WHERE name IN (N'fnEsNestoViejo', N'fnNestoViejoPermitido', N'fnNestoViejoModo', N'NestoViejoSombra');
GO

------------------------------------------------------------------------------------------------------------------------------
-- 1) ¿Es Nesto viejo?
------------------------------------------------------------------------------------------------------------------------------
CREATE OR ALTER FUNCTION dbo.fnEsNestoViejo()
RETURNS bit
WITH SCHEMABINDING
AS
BEGIN
    -- NestoAPI#590 09/11/26: Nesto viejo (ODBC) se conecta con APP_NAME() = 'Nesto'. Interruptor general: RETURN 0.
    RETURN CASE WHEN APP_NAME() = N'Nesto' THEN 1 ELSE 0 END;
END
GO

------------------------------------------------------------------------------------------------------------------------------
-- 2) Excepción por usuario: ParametrosUsuario 'NestoViejo_<Operacion>' = '1'
------------------------------------------------------------------------------------------------------------------------------
CREATE OR ALTER FUNCTION dbo.fnNestoViejoPermitido(@Operacion varchar(38))
RETURNS bit
AS
BEGIN
    -- NestoAPI#590 09/11/26. Usuario sin dominio, como lo guarda Nesto viejo ('NUEVAVISION\Alfredo' -> 'Alfredo').
    -- ORIGINAL_LOGIN() y no SYSTEM_USER: es el login de la conexión aunque un procedimiento cambie de contexto (EXECUTE AS).
    DECLARE @login nvarchar(128) = ORIGINAL_LOGIN();
    DECLARE @usuario varchar(30) = LEFT(SUBSTRING(@login, CHARINDEX(N'\', @login) + 1, 128), 30);
    DECLARE @clave char(50) = 'NestoViejo_' + @Operacion;
    DECLARE @valor varchar(162);

    SELECT @valor = RTRIM(LTRIM(Valor))
    FROM dbo.ParametrosUsuario
    WHERE Empresa = '1' AND Clave = @clave AND Usuario = @usuario;   -- '=' no mira los espacios del final

    IF @valor IS NULL
        SELECT @valor = RTRIM(LTRIM(Valor))
        FROM dbo.ParametrosUsuario
        WHERE Empresa = '1' AND Clave = @clave AND Usuario = '(defecto)';

    RETURN CASE WHEN @valor = '1' THEN 1 ELSE 0 END;
END
GO

------------------------------------------------------------------------------------------------------------------------------
-- 3) Modo del bloqueo: 'Bloqueo' o 'Sombra' (fila «(defecto)» 'NestoViejo_<Operacion>_Modo')
------------------------------------------------------------------------------------------------------------------------------
CREATE OR ALTER FUNCTION dbo.fnNestoViejoModo(@Operacion varchar(38))
RETURNS varchar(10)
AS
BEGIN
    -- NestoAPI#590 09/11/26. Sin fila o con un valor desconocido: 'Sombra' (deja hacer y lo apunta).
    DECLARE @valor varchar(162);
    SELECT @valor = RTRIM(LTRIM(Valor))
    FROM dbo.ParametrosUsuario
    WHERE Empresa = '1' AND Clave = 'NestoViejo_' + @Operacion + '_Modo' AND Usuario = '(defecto)';

    RETURN CASE WHEN @valor = 'Bloqueo' THEN 'Bloqueo' ELSE 'Sombra' END;
END
GO

------------------------------------------------------------------------------------------------------------------------------
-- 4) El rastro
------------------------------------------------------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.NestoViejoSombra', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.NestoViejoSombra (
        Id        int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_NestoViejoSombra PRIMARY KEY,
        Fecha     datetime2(0)  NOT NULL CONSTRAINT DF_NestoViejoSombra_Fecha   DEFAULT (SYSDATETIME()),
        Usuario   nvarchar(128) NOT NULL CONSTRAINT DF_NestoViejoSombra_Usuario DEFAULT (ORIGINAL_LOGIN()),
        Operacion varchar(40)   NOT NULL,
        Resultado varchar(10)   NOT NULL,   -- Sombra | Permitido | Bloqueado
        Tabla     sysname       NOT NULL,
        Clave     nvarchar(100) NULL,
        Detalle   nvarchar(2000) NULL,
        Equipo    nvarchar(128) NULL CONSTRAINT DF_NestoViejoSombra_Equipo DEFAULT (HOST_NAME())
    );
    CREATE INDEX IX_NestoViejoSombra_Operacion_Fecha ON dbo.NestoViejoSombra (Operacion, Fecha);
    PRINT 'dbo.NestoViejoSombra creada.';
END
ELSE
    PRINT 'dbo.NestoViejoSombra ya existía: no se toca.';
GO

-- DESPUÉS: tienen que salir las cuatro
SELECT 'DESPUES' Momento, name, type_desc, create_date, modify_date
FROM sys.objects
WHERE name IN (N'fnEsNestoViejo', N'fnNestoViejoPermitido', N'fnNestoViejoModo', N'NestoViejoSombra');

-- Desde SSMS (APP_NAME distinto de 'Nesto') tiene que dar EsNestoViejo = 0, Permitido = 0 y Modo = 'Sombra'.
-- Para probar como si fuera Nesto viejo: nueva conexión de SSMS con «Opciones > Parámetros de conexión adicionales»:
-- Application Name=Nesto  (APP_NAME() pasa a ser 'Nesto' y fnEsNestoViejo() da 1).
SELECT APP_NAME() Programa, ORIGINAL_LOGIN() Login, dbo.fnEsNestoViejo() EsNestoViejo,
       dbo.fnNestoViejoPermitido('Prueba') Permitido, dbo.fnNestoViejoModo('Prueba') Modo;
GO
