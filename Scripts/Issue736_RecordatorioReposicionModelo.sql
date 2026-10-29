/*
    NestoAPI#736: recordatorio de reposición — tope de días, puerta con el modelo de propensión y conversión.

    Qué hace (idempotente: se puede lanzar dos veces):
      1. Añade a dbo.RecordatoriosReposicionEnvios (NestoAPI#532) dos columnas NULLABLE:
           ProbabilidadModelo decimal(6, 5) NULL  -- la P1 del modelo 1 de propensión (#671) cuando se le recordó
           FueraPorModelo     bit           NULL  -- si la P1 quedaba por debajo del umbral (en Sombra se envía igual)
         NULL = registrado sin puntuar (antes de #736, puerta apagada, sin modelo o producto que el modelo no puntúa).
      2. Parámetros de (defecto), sin pisar los que ya existan:
           RecordatorioReposicionTopeDias      = 240     -- días desde la última compra; «0» o vacío = sin tope
           RecordatorioReposicionPuertaModelo  = Sombra  -- Apagado | Sombra | Activo
           RecordatorioReposicionUmbralModelo  = 0,15    -- P1 mínima (en Activo, lo de menos no se recuerda)
         Sin fila, la API usa esos mismos valores: crearlas es para verlas y cambiarlas desde Nesto/SSMS.

    ORDEN: da igual. La API nueva sin el script no rompe nada: no registra la P1 (sigue registrando el envío como siempre) y
    el informe de conversión sale sin el desglose por P1. El script sin la API nueva tampoco: las columnas quedan a NULL.

    Es DDL: desde SSMS con `sa`. La API (cuenta [NUEVAVISION\RDS2016$]) ya tiene SELECT, INSERT en la tabla (Issue532): las
    columnas nuevas no necesitan GRANT aparte.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.RecordatoriosReposicionEnvios') IS NULL
    RAISERROR('Falta dbo.RecordatoriosReposicionEnvios: lanzar antes Scripts/Issue532_RecordatorioReposicionActivo.sql', 16, 1);
GO

IF OBJECT_ID('dbo.RecordatoriosReposicionEnvios') IS NOT NULL AND COL_LENGTH('dbo.RecordatoriosReposicionEnvios', 'ProbabilidadModelo') IS NULL
    ALTER TABLE dbo.RecordatoriosReposicionEnvios ADD ProbabilidadModelo decimal(6, 5) NULL;
GO

IF OBJECT_ID('dbo.RecordatoriosReposicionEnvios') IS NOT NULL AND COL_LENGTH('dbo.RecordatoriosReposicionEnvios', 'FueraPorModelo') IS NULL
    ALTER TABLE dbo.RecordatoriosReposicionEnvios ADD FueraPorModelo bit NULL;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ParámetrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'RecordatorioReposicionTopeDias')
    INSERT INTO dbo.ParámetrosUsuario (Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', '(defecto)', 'RecordatorioReposicionTopeDias', '240', 'NestoAPI#736', GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.ParámetrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'RecordatorioReposicionPuertaModelo')
    INSERT INTO dbo.ParámetrosUsuario (Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', '(defecto)', 'RecordatorioReposicionPuertaModelo', 'Sombra', 'NestoAPI#736', GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.ParámetrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'RecordatorioReposicionUmbralModelo')
    INSERT INTO dbo.ParámetrosUsuario (Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', '(defecto)', 'RecordatorioReposicionUmbralModelo', '0,15', 'NestoAPI#736', GETDATE());
GO

-- Comprobación
SELECT COL_LENGTH('dbo.RecordatoriosReposicionEnvios', 'ProbabilidadModelo') AS ProbabilidadModelo,
       COL_LENGTH('dbo.RecordatoriosReposicionEnvios', 'FueraPorModelo') AS FueraPorModelo;
SELECT Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación]
FROM dbo.ParámetrosUsuario WHERE Clave LIKE 'RecordatorioReposicion%' AND Usuario = '(defecto)';
GO

-- PASAR LA PUERTA A ACTIVO (cuando la sombra y la conversión digan qué umbral; GET api/CorreosPostCompra/Reposicion/Conversion):
-- UPDATE dbo.ParámetrosUsuario SET Valor = 'Activo', Usuario2 = 'NestoAPI#736', [Fecha Modificación] = GETDATE()
-- WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'RecordatorioReposicionPuertaModelo';

-- CAMBIAR EL UMBRAL:
-- UPDATE dbo.ParámetrosUsuario SET Valor = '0,20', Usuario2 = 'NestoAPI#736', [Fecha Modificación] = GETDATE()
-- WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'RecordatorioReposicionUmbralModelo';

-- CAMBIAR EL TOPE (0 o vacío = sin tope):
-- UPDATE dbo.ParámetrosUsuario SET Valor = '180', Usuario2 = 'NestoAPI#736', [Fecha Modificación] = GETDATE()
-- WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'RecordatorioReposicionTopeDias';

