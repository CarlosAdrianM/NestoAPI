-- 26/11/26. Lanzar como sa en NV.
-- Descuento pactado que pide Israel (26/11/26): cliente 35253, de Lisap «de momento la línea Keraplant con el 50 %».
-- Fila por CLIENTE + FAMILIA + FILTRO de nombre, como las del 32526 y el 9037: vale para los productos de Lisap cuyo nombre
-- EMPIEZA por «KERAPLANT» (125 referencias vivas hoy; el material promocional de Keraplant no empieza así y queda fuera).
-- Con esto el motor de precios le pone el 50 % solo y la validación (estricta desde el 25/11) lo autoriza.
-- No toca ninguna fila que ya exista; se puede relanzar.
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS (SELECT 1 FROM dbo.Clientes c WHERE c.Empresa = '1' AND RTRIM(c.[Nº Cliente]) = '35253')
BEGIN
    RAISERROR('El cliente 35253 no existe: no se ha creado nada.', 16, 1);
    RETURN;
END

BEGIN TRANSACTION;

INSERT INTO dbo.DescuentosProducto (Empresa, [Nº Cliente], Familia, FiltroProducto, CantidadMínima, Descuento, AudienciaOferta, Usuario, [Fecha Modificación])
SELECT '1', '35253', 'Lisap', 'KERAPLANT', 1, 0.50, 0, 'NUEVAVISION\Carlos', GETDATE()
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.DescuentosProducto d
    WHERE d.Empresa = '1' AND RTRIM(d.[Nº Cliente]) = '35253' AND RTRIM(d.Familia) = 'Lisap' AND RTRIM(d.FiltroProducto) = 'KERAPLANT'
      AND d.[Nº Producto] IS NULL AND d.GrupoProducto IS NULL AND d.NºProveedor IS NULL);

PRINT CONCAT('Filas creadas: ', @@ROWCOUNT);

COMMIT TRANSACTION;
GO

SELECT d.[Nº Orden], RTRIM(d.[Nº Cliente]) Cliente, RTRIM(d.Familia) Familia, RTRIM(d.FiltroProducto) Filtro, d.CantidadMínima, d.Descuento, RTRIM(d.Usuario) Usuario, d.[Fecha Modificación]
FROM dbo.DescuentosProducto d
WHERE d.Empresa = '1' AND RTRIM(d.[Nº Cliente]) = '35253';
SELECT COUNT(*) AS ProductosALosQueAplica FROM dbo.Productos p WHERE p.Empresa = '1' AND p.Estado >= 0 AND RTRIM(p.Familia) = 'Lisap' AND p.Nombre LIKE 'KERAPLANT%';
GO
