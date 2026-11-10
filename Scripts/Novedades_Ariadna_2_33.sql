/*
    Novedades de Ariadna 2.33 (10/11/26): parte Ariadna de NestoAPI#828 (F8 factura e imprime el papel de toda la entrega, «Meta en la
    caja la factura…» y «Reintentar impresión», también la carta del cheque regalo) y los enlaces de Maps y WhatsApp en Android 11 o
    posterior. Necesita la API del 10/11 (master tras 12f00c3c). Ejecutar en SSMS contra NV como sa cuando la 2.33 llegue a la prueba
    cerrada. Idempotente (compara por Ámbito + Título).
    NO duplicar: la sugerencia 1165 (Miguel, «Ir» a la parada no abría Google Maps en su móvil) se marca Implementada en 2.33 por PUT
    (la sugerencia ES la novedad); aquí no lleva fila. La 973 (Alfredo, la factura física) es de Nesto y va con la 1.10.59.0; aquí sí
    lleva fila lo que cambia en la PDA.
    Se omite a propósito: que la ruta propia siga facturando e imprimiendo sus copias con F8 como antes del #828 (no cambia nada para el mozo).
*/
SET NOCOUNT ON;
USE NV;
DECLARE @n TABLE (Orden int, Categoria nvarchar(20), Titulo nvarchar(200), Descripcion nvarchar(max), Perfiles nvarchar(200));
INSERT INTO @n VALUES
(1, N'Nuevo', N'Gracias, Alfredo y Miguel',
 N'La factura en papel que sale al hacer la etiqueta (Alfredo) y el mapa y los enlaces de pago que no se abrían en el móvil (Miguel) salen de lo que nos han contado por «Avisar». Muchas gracias: sigan contándonos.',
 NULL),
(2, N'Nuevo', N'Packing: la factura en papel sale con la etiqueta y Ariadna dice «Meta en la caja»',
 N'Al hacer la etiqueta con F8, se factura el pedido y también los demás pedidos que van en la misma entrega, y si alguno lleva factura en papel sale en ese momento por la impresora. El panel lo dice destacado: «Meta en la caja la factura NV… (en «impresora»).», y si hay carta del cheque regalo, también. Si la impresora falla, la etiqueta sale igual y el panel ofrece «Reintentar impresión» (F3 o el botón): vuelve a imprimir lo que faltaba sin facturar otra vez ni repetir la etiqueta. Un pedido retenido o aplazado de esa entrega no se factura, y el panel lo dice.',
 N'Almacén');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT '2.33', '20261110', n.Categoria, n.Titulo, n.Descripcion, 'Ariadna', 1, 'sa', n.Perfiles
FROM @n n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = n.Titulo)
ORDER BY n.Orden;
SELECT @@ROWCOUNT AS NovedadesAriadnaInsertadasAhora;
