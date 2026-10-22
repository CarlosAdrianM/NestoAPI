/*
    NestoAPI#532 (modo Activo): el recordatorio de reposición por correo empieza a escribir de verdad a los clientes.

    Qué crea (idempotente: se puede lanzar dos veces):
      1. dbo.RecordatoriosReposicionTextos  - el texto de cada semana (OpenAI o la plantilla fija), con su versión.
      2. dbo.RecordatoriosReposicionEnvios  - una fila por cliente y producto recordado: Grupo 'Enviado' (le llegó el correo)
                                              o 'Control' (le tocaba, es del grupo de control y NO se le escribió). Sirve
                                              para no repetir (un recordatorio por producto y compra, un correo cada 14 días)
                                              y para medir contra el grupo de control (Issue532_MedicionRecordatorioReposicion.sql).
      3. dbo.CorreosPostCompraBajas         - quien ha pedido no recibir estos correos. Se meten A MANO cuando alguien
                                              contesta pidiendo la baja (por cliente, por correo o por los dos). De momento
                                              solo la mira el recordatorio de reposición.
      4. Parámetro RecordatorioReposicionVendedores de (defecto): a los clientes de qué vendedores se les escribe en Activo
         («(NULL)» = cliente sin vendedor; sin fila o vacío = a NINGUNO).
    El interruptor (RecordatorioReposicion) NO se toca: sigue como esté (Sombra). Para encenderlo, al final.

    ORDEN: 1) este script; 2) publicar el API; 3) cuando se quiera, pasar a Activo (línea comentada al final).
    El API nuevo sin el script no rompe nada: en Activo, si faltan las tablas, NO escribe a nadie y lo dice en el resumen.

    Es DDL: desde SSMS con `sa` (el login nuevavision no puede crear tablas ni dar permisos). La API corre como la cuenta
    máquina [NUEVAVISION\RDS2016$]: lee y escribe envíos y textos y solo lee las bajas.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.RecordatoriosReposicionTextos') IS NULL
BEGIN
    CREATE TABLE dbo.RecordatoriosReposicionTextos (
        Id int IDENTITY(1, 1) NOT NULL,
        Empresa char(3) NOT NULL,
        Semana date NOT NULL,                           -- el jueves del envío
        Origen varchar(10) NOT NULL,                    -- OpenAI | Fija
        Version varchar(30) NOT NULL,                   -- ia-2026-10-29 | fija-1
        Asunto nvarchar(200) NOT NULL,                  -- con {PRODUCTO}
        Entrada nvarchar(1000) NOT NULL,                -- con {PRODUCTOS}
        Cierre nvarchar(1000) NOT NULL,                 -- con {VENDEDOR}
        MotivoFija nvarchar(400) NULL,                  -- por qué no valió lo de OpenAI
        FechaCreacion datetime NOT NULL CONSTRAINT DF_RecordatoriosReposicionTextos_FechaCreacion DEFAULT (GETDATE()),

        CONSTRAINT PK_RecordatoriosReposicionTextos PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_RecordatoriosReposicionTextos_Origen CHECK (Origen IN ('OpenAI', 'Fija'))
    );
END
GO

IF OBJECT_ID('dbo.RecordatoriosReposicionEnvios') IS NULL
BEGIN
    CREATE TABLE dbo.RecordatoriosReposicionEnvios (
        Id int IDENTITY(1, 1) NOT NULL,
        Empresa char(3) NOT NULL,
        Cliente varchar(10) NOT NULL,
        Email varchar(255) NULL,
        Vendedor varchar(10) NULL,                      -- el del cliente ese día (NULL = sin vendedor)
        Producto varchar(15) NOT NULL,
        NombreProducto nvarchar(100) NULL,
        UltimaCompra date NOT NULL,                     -- la que tenía cuando se le recordó
        IntervaloDias int NOT NULL,                     -- su ritmo
        CantidadHabitual int NOT NULL,
        Grupo varchar(10) NOT NULL,                     -- Enviado | Control
        Fecha datetime NOT NULL CONSTRAINT DF_RecordatoriosReposicionEnvios_Fecha DEFAULT (GETDATE()),
        TextoId int NULL,
        VersionTexto varchar(30) NOT NULL,

        CONSTRAINT PK_RecordatoriosReposicionEnvios PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_RecordatoriosReposicionEnvios_Grupo CHECK (Grupo IN ('Enviado', 'Control')),
        CONSTRAINT FK_RecordatoriosReposicionEnvios_Texto FOREIGN KEY (TextoId) REFERENCES dbo.RecordatoriosReposicionTextos (Id)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RecordatoriosReposicionEnvios_Cliente' AND object_id = OBJECT_ID('dbo.RecordatoriosReposicionEnvios'))
    CREATE INDEX IX_RecordatoriosReposicionEnvios_Cliente ON dbo.RecordatoriosReposicionEnvios (Empresa, Cliente, Producto, Fecha);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RecordatoriosReposicionEnvios_Fecha' AND object_id = OBJECT_ID('dbo.RecordatoriosReposicionEnvios'))
    CREATE INDEX IX_RecordatoriosReposicionEnvios_Fecha ON dbo.RecordatoriosReposicionEnvios (Empresa, Fecha) INCLUDE (Cliente, Producto, Grupo);
GO

IF OBJECT_ID('dbo.CorreosPostCompraBajas') IS NULL
BEGIN
    CREATE TABLE dbo.CorreosPostCompraBajas (
        Id int IDENTITY(1, 1) NOT NULL,
        Empresa char(3) NOT NULL CONSTRAINT DF_CorreosPostCompraBajas_Empresa DEFAULT ('1'),
        Cliente varchar(10) NULL,                       -- todos sus correos
        Email varchar(255) NULL,                        -- esta dirección, sea de quien sea
        Fecha datetime NOT NULL CONSTRAINT DF_CorreosPostCompraBajas_Fecha DEFAULT (GETDATE()),
        Motivo nvarchar(200) NULL,
        Usuario varchar(50) NULL,

        CONSTRAINT PK_CorreosPostCompraBajas PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_CorreosPostCompraBajas_ClienteOEmail CHECK (Cliente IS NOT NULL OR Email IS NOT NULL)
    );
END
GO

GRANT SELECT, INSERT ON dbo.RecordatoriosReposicionTextos TO [NUEVAVISION\RDS2016$];
GRANT SELECT, INSERT ON dbo.RecordatoriosReposicionEnvios TO [NUEVAVISION\RDS2016$];
GRANT SELECT ON dbo.CorreosPostCompraBajas TO [NUEVAVISION\RDS2016$];
GO

-- Vendedores a cuyos clientes se escribe en Activo (no se pisa si ya existe)
IF NOT EXISTS (SELECT 1 FROM dbo.ParámetrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'RecordatorioReposicionVendedores')
    INSERT INTO dbo.ParámetrosUsuario (Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', '(defecto)', 'RecordatorioReposicionVendedores', 'NV,(NULL),AGR,ISR,PCN,CAM,LA,DO', 'NestoAPI#532', GETDATE());
GO

-- Comprobación
SELECT OBJECT_ID('dbo.RecordatoriosReposicionTextos') AS Textos, OBJECT_ID('dbo.RecordatoriosReposicionEnvios') AS Envios,
       OBJECT_ID('dbo.CorreosPostCompraBajas') AS Bajas;
SELECT Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación]
FROM dbo.ParámetrosUsuario WHERE Clave LIKE 'RecordatorioReposicion%';
GO

-- ENCENDER DE VERDAD (los jueves a las 9:00 escribe a los clientes de esos vendedores; el resumen sigue llegando a Informática):
-- UPDATE dbo.ParámetrosUsuario SET Valor = 'Activo', Usuario2 = 'NestoAPI#532', [Fecha Modificación] = GETDATE()
-- WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'RecordatorioReposicion';

-- VOLVER A SOMBRA (solo el resumen a Informática, no se escribe a nadie):
-- UPDATE dbo.ParámetrosUsuario SET Valor = 'Sombra', Usuario2 = 'NestoAPI#532', [Fecha Modificación] = GETDATE()
-- WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'RecordatorioReposicion';

-- APAGAR (no hace nada, ni el resumen):
-- UPDATE dbo.ParámetrosUsuario SET Valor = 'Apagado', Usuario2 = 'NestoAPI#532', [Fecha Modificación] = GETDATE()
-- WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'RecordatorioReposicion';

-- CAMBIAR LOS VENDEDORES («(NULL)» = sin vendedor; vacío = ninguno):
-- UPDATE dbo.ParámetrosUsuario SET Valor = 'NV,(NULL),AGR,ISR,PCN,CAM,LA,DO', Usuario2 = 'NestoAPI#532', [Fecha Modificación] = GETDATE()
-- WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'RecordatorioReposicionVendedores';

-- DAR DE BAJA a quien conteste pidiéndolo (por correo, por cliente o por los dos):
-- INSERT INTO dbo.CorreosPostCompraBajas (Empresa, Cliente, Email, Motivo, Usuario)
-- VALUES ('1', '15191', 'correo@cliente.es', N'Contestó al recordatorio de reposición pidiendo la baja', 'Carlos');
