/*
    Novedades de Ariadna 2.30 (05/11/26): Ariadna#96 (familia y tamaño sin recortar en la tarjeta del producto, sugerencia 1054 de Paloma);
    API: NestoAPI#823 (una persona, una identidad: ya se puede contabilizar el inventario desde Ariadna sin «Fecha no permitida»),
    #825 (los pedidos de clientes de factura simplificada van solos en el packing, sugerencia 1056 de Santiago), #797/#762 sin cambios.
    Necesita la API del 05/11 (master tras 687906f1).
    Ejecutar en SSMS contra NV como sa cuando la 2.30 llegue a la prueba cerrada. Idempotente (compara por Ámbito + Título).
    NO duplicar: las sugerencias 1054 (Ariadna#96) y 1056 (#825) las marca Implementadas en 2.30 el OneShot (la sugerencia ES la
    novedad); aquí no llevan fila.
*/
SET NOCOUNT ON;
USE NV;
DECLARE @n TABLE (Orden int, Categoria nvarchar(20), Titulo nvarchar(200), Descripcion nvarchar(max), Perfiles nvarchar(200));
INSERT INTO @n VALUES
(1, N'Nuevo', N'Gracias, Paloma y Santiago',
 N'La familia y el tamaño que se recortaban en el móvil y los pedidos de Amazon y de la tienda online mezclados en una misma entrega del packing salen de lo que nos habéis contado por «Avisar». Seguid contándonos.',
 NULL),
(2, N'Corregido', N'Inventario: ya se puede contabilizar desde Ariadna',
 N'Contabilizar el inventario desde Ariadna daba «Fecha no permitida» a quien entra con su usuario sin dominio. La API reconoce ahora que es la misma persona que su usuario de Nesto y usa sus fechas permitidas. La prueba del inventario (una referencia con «Evolutivo» y F6) ya se puede hacer en Ariadna; el día 21 es el inventario de verdad.',
 N'Almacén,Tiendas');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT '2.30', '2026-11-05', n.Categoria, n.Titulo, n.Descripcion, 'Ariadna', 1, 'sa', n.Perfiles
FROM @n n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = n.Titulo)
ORDER BY n.Orden;
SELECT @@ROWCOUNT AS NovedadesAriadnaInsertadasAhora;
