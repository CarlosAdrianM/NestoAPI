/*
    NestoAPI#857j (Carlos 26/11/26, caso SEUR «Identificador de envío: A-2609942 … será entregado HOY», de Iberocel): rastro de los
    correos de una AGENCIA CONOCIDA que el lector no reconoce.

    Hasta ahora, un correo de la propia agencia (SEUR, UPS, MBE…) al que su clase no le saca ni código ni entrega ni estado no se
    guardaba ni dejaba rastro: un formato nuevo solo se descubría si alguien lo veía en el buzón de Compras. El job
    compras-correos-seguimiento lo apunta ahora aquí, APARTE de dbo.ComprasCorreosSeguimiento (no sale en «por asignar», no se aplica,
    no se reenvía, no avisa): fecha, remitente, agencia, asunto y los primeros 2.000 caracteres del texto. Nunca la publicidad ni las
    encuestas de la agencia que el analizador ya sabe descartar. Idempotente por MensajeId (como los demás).
    Reanalizar (POST api/Compras/AgenciasEntrada/Correos/Reanalizar) los vuelve a pasar: si ahora se reconocen, se guardan como correos
    normales y aquí quedan con ReconocidoEl y CorreoId. Ver: GET api/Compras/AgenciasEntrada/Correos/SinReconocer?dias=30.

    ORDEN: después de Issue857_ComprasCorreosSeguimiento.sql. Puede ir ANTES o DESPUÉS de publicar la API: sin la tabla, el job no apunta
    nada (ELMAH una vez) y el endpoint nuevo da 503; lo vuelve a comprobar cada 10 minutos, sin reiniciar.

    Lanzar como sa en NV. Idempotente: se puede relanzar. Permisos: GRANT a [NUEVAVISION\RDS2016$] (como ComprasOrigenesAgencia).
*/
SET NOCOUNT ON;
USE NV;
GO
IF OBJECT_ID(N'dbo.ComprasCorreosSeguimiento', N'U') IS NULL
    RAISERROR(N'No existe dbo.ComprasCorreosSeguimiento: lance antes Scripts/Issue857_ComprasCorreosSeguimiento.sql. No se ha cambiado nada.', 16, 1);
GO
IF OBJECT_ID(N'dbo.ComprasCorreosSeguimiento', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.ComprasCorreosSinReconocer', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ComprasCorreosSinReconocer (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ComprasCorreosSinReconocer PRIMARY KEY CLUSTERED,
        MensajeId nvarchar(450) NOT NULL,        -- la misma clave que dbo.ComprasCorreosSeguimiento
        Recibido datetime NOT NULL,
        Remitente nvarchar(255) NULL,
        Agencia nvarchar(50) NOT NULL,           -- la agencia cuyo correo es (por el remitente)
        Asunto nvarchar(255) NULL,
        Extracto nvarchar(2000) NULL,            -- los primeros 2.000 caracteres del texto
        FechaAlta datetime NOT NULL CONSTRAINT DF_ComprasCorreosSinReconocer_FechaAlta DEFAULT (GETDATE()),
        ReconocidoEl datetime NULL,              -- Reanalizar lo ha reconocido y guardado como correo normal
        CorreoId int NULL                        -- su Id en dbo.ComprasCorreosSeguimiento
    );
    CREATE UNIQUE INDEX UX_ComprasCorreosSinReconocer_MensajeId ON dbo.ComprasCorreosSinReconocer (MensajeId);
    CREATE INDEX IX_ComprasCorreosSinReconocer_Recibido ON dbo.ComprasCorreosSinReconocer (Recibido) INCLUDE (ReconocidoEl);
END
GO
IF OBJECT_ID(N'dbo.ComprasCorreosSinReconocer', N'U') IS NOT NULL
    GRANT SELECT, INSERT, UPDATE ON dbo.ComprasCorreosSinReconocer TO [NUEVAVISION\RDS2016$];
GO
-- Comprobación
SELECT CASE WHEN OBJECT_ID(N'dbo.ComprasCorreosSinReconocer', N'U') IS NOT NULL
            THEN N'OK: NestoAPI#857j (tabla ComprasCorreosSinReconocer)'
            ELSE N'FALTA algo (¿existe dbo.ComprasCorreosSeguimiento?)' END AS Resultado;
GO
