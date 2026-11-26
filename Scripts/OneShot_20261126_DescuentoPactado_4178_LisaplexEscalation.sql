-- 26/11/26. Lanzar como sa en NV.
-- Descuento pactado que pide Israel (26/11/26): cliente 4178 (SILVIA TORRECILLA MORENO), «la línea Escalation de tintes» de Lisap al 50 %.
-- OJO con el filtro: el motor casa por cómo EMPIEZA el nombre, y los tintes Escalation vivos NO empiezan por «ESCALATION» (esos 75 son
-- referencias antiguas, todas anuladas): se llaman «LISAPLEX ESCALATION …» (98 referencias vivas hoy, todas del subgrupo de tintes).
-- Por eso el filtro es «LISAPLEX ESCALATION». Quedan fuera el resto de Lisaplex (Filter Color, Pastel, Bond Saver…) y el material
-- promocional (carta de color, catálogo, guía técnica), que no empieza así.
-- No toca ninguna fila que ya exista; se puede relanzar.
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS (SELECT 1 FROM dbo.Clientes c WHERE c.Empresa = '1' AND RTRIM(c.[Nº Cliente]) = '4178')
BEGIN
    RAISERROR('El cliente 4178 no existe: no se ha creado nada.', 16, 1);
    RETURN;
END

BEGIN TRANSACTION;

INSERT INTO dbo.DescuentosProducto (Empresa, [Nº Cliente], Familia, FiltroProducto, CantidadMínima, Descuento, AudienciaOferta, Usuario, [Fecha Modificación])
SELECT '1', '4178', 'Lisap', 'LISAPLEX ESCALATION', 1, 0.50, 0, 'NUEVAVISION\Carlos', GETDATE()
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.DescuentosProducto d
    WHERE d.Empresa = '1' AND RTRIM(d.[Nº Cliente]) = '4178' AND RTRIM(d.Familia) = 'Lisap' AND RTRIM(d.FiltroProducto) = 'LISAPLEX ESCALATION'
      AND d.[Nº Producto] IS NULL AND d.GrupoProducto IS NULL AND d.NºProveedor IS NULL);

PRINT CONCAT('Filas creadas: ', @@ROWCOUNT);

COMMIT TRANSACTION;
GO

SELECT d.[Nº Orden], RTRIM(d.[Nº Cliente]) Cliente, RTRIM(d.Familia) Familia, RTRIM(d.FiltroProducto) Filtro, d.CantidadMínima, d.Descuento, RTRIM(d.Usuario) Usuario, d.[Fecha Modificación]
FROM dbo.DescuentosProducto d
WHERE d.Empresa = '1' AND RTRIM(d.[Nº Cliente]) = '4178';
SELECT COUNT(*) AS ProductosALosQueAplica FROM dbo.Productos p WHERE p.Empresa = '1' AND p.Estado >= 0 AND RTRIM(p.Familia) = 'Lisap' AND p.Nombre LIKE 'LISAPLEX ESCALATION%';
GO
