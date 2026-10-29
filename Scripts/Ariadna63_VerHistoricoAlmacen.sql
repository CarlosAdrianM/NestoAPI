/*
    Ariadna#63 (sugerencias 872 y 873, Carlos 29/10/26): histórico de pickings y packings en Ariadna («saber si algo se picó o no»).

    1. Parámetro de usuario nuevo VerHistoricoAlmacen ("1" = puede ver api/Almacen/Historico). La API lo lee SOLO de la fila
       del propio usuario (LeerParametroSinDefecto): la de «(defecto)» no da permiso a nadie. Aun así se crea con "0" para que
       Nesto no falle si algún día lo lee con leerParametro. Dirección e Informática lo ven siempre, sin parámetro.
       Se le da a Alfredo y a Santiago (usuario sin dominio, como en el resto de ParametrosUsuario).
    2. Dos índices para las listas por fechas (máx. 31 días). Sin ellos la API funciona igual (las tablas son pequeñas), pero
       así no recorre PreparacionEscaneos entera cuando crezca.

    Para dárselo a alguien más:
        INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
        VALUES ('1', 'VerHistoricoAlmacen', 'Fulano', '1', 'Ariadna63', GETDATE());

    Lanzar en SSMS contra NV como sa (los índices son DDL). Idempotente: se puede lanzar dos veces.
    Se puede lanzar antes o después del deploy. Sin GRANT nuevo: ParametrosUsuario y las dos tablas ya los lee NestoAPI.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

------------------------------------------------------------------------------------------------
-- 1. El parámetro VerHistoricoAlmacen
------------------------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'VerHistoricoAlmacen' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'VerHistoricoAlmacen', '(defecto)', '0', 'Ariadna63', GETDATE());

IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'VerHistoricoAlmacen' AND Usuario = 'Alfredo')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'VerHistoricoAlmacen', 'Alfredo', '1', 'Ariadna63', GETDATE());
ELSE
    UPDATE ParametrosUsuario SET Valor = '1', Usuario2 = 'Ariadna63', [Fecha Modificación] = GETDATE()
    WHERE Empresa = '1' AND Clave = 'VerHistoricoAlmacen' AND Usuario = 'Alfredo' AND RTRIM(Valor) <> '1';

IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'VerHistoricoAlmacen' AND Usuario = 'Santiago')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'VerHistoricoAlmacen', 'Santiago', '1', 'Ariadna63', GETDATE());
ELSE
    UPDATE ParametrosUsuario SET Valor = '1', Usuario2 = 'Ariadna63', [Fecha Modificación] = GETDATE()
    WHERE Empresa = '1' AND Clave = 'VerHistoricoAlmacen' AND Usuario = 'Santiago' AND RTRIM(Valor) <> '1';
GO

------------------------------------------------------------------------------------------------
-- 2. Índices para las listas por fechas
------------------------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.PreparacionEscaneos') AND name = 'IX_PreparacionEscaneos_Fecha')
    CREATE INDEX IX_PreparacionEscaneos_Fecha ON dbo.PreparacionEscaneos (Empresa, Fase, FechaEscaneo)
        INCLUDE (TipoOrigen, NumeroOrigen, Pedido);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.EnviosAgenciaBultos') AND name = 'IX_EnviosAgenciaBultos_Fecha')
    CREATE INDEX IX_EnviosAgenciaBultos_Fecha ON dbo.EnviosAgenciaBultos (Empresa, FechaRegistro)
        INCLUDE (Pedido);
GO

-- VERIFICACIÓN: tres filas ((defecto) = 0, Alfredo = 1, Santiago = 1) y los dos índices
SELECT Empresa, Clave, Usuario, Valor FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'VerHistoricoAlmacen';
SELECT OBJECT_NAME(object_id) AS Tabla, name AS Indice FROM sys.indexes
WHERE name IN ('IX_PreparacionEscaneos_Fecha', 'IX_EnviosAgenciaBultos_Fecha');
GO
