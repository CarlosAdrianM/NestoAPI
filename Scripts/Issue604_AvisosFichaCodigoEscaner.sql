-- NestoAPI#604: en los avisos de ficha desde Ariadna, el código que manda el mozo puede venir leído con el
-- escáner o tecleado (o copiado de la tarjeta). Se guarda para decirlo en el correo y en el buzón:
--   CodigoLeidoConEscaner (bit NULL)  1 = leído con el escáner · 0 = tecleado · NULL = no se sabe (Ariadna antigua)
--
-- Además, un índice para saber si hoy se ha recogido o empaquetado a mano un producto (PreparacionEscaneos,
-- Metodo MANUAL): el aviso lo dice cuando el código enviado es el mismo de la ficha.
--
-- ORDEN: lanzar ANTES de publicar el API que trae el cambio (el API nuevo ya escribe la columna).
-- Se puede lanzar dos veces sin problema.
--
-- PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
--
-- Es DDL: desde SSMS con `sa` (el login `nuevavision` no tiene ALTER).

USE NV;
GO

IF COL_LENGTH('dbo.AvisosFichaProducto', 'CodigoLeidoConEscaner') IS NULL
BEGIN
    ALTER TABLE dbo.AvisosFichaProducto ADD CodigoLeidoConEscaner bit NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PreparacionEscaneos_Producto' AND object_id = OBJECT_ID('dbo.PreparacionEscaneos'))
BEGIN
    CREATE INDEX IX_PreparacionEscaneos_Producto ON dbo.PreparacionEscaneos (Empresa, Producto, FechaEscaneo) INCLUDE (Metodo, Fase);
END
GO

-- BD de negocio (NestoConnection): el API accede con la cuenta de máquina (los permisos de la tabla no cambian).
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.AvisosFichaProducto TO [NUEVAVISION\RDS2016$];
GO

SELECT COL_LENGTH('dbo.AvisosFichaProducto', 'CodigoLeidoConEscaner') AS CodigoLeidoConEscaner,
       (SELECT COUNT(*) FROM sys.indexes WHERE name = 'IX_PreparacionEscaneos_Producto') AS IndiceProducto;
