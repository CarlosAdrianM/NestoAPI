/*
    Novedades de la versión 1.10.25.1 (02/09/2026).

    Solo sube la última cifra: es una tanda de correcciones, sin ventana ni funcionalidad nueva.

    SE OMITE TODO LO DEMÁS A PROPÓSITO (nada de esto lo nota el usuario de Nesto):
      - El alta de tarjeta y la descripción de las tarjetas guardadas en la app de clientes
        (NestoAPI#178, TNV#58/#59): es de la app, se cuenta al equipo de la app.
      - El 400 de GET Vendedores con empresa vacía y el guard del SelectorVendedor en la ficha
        de cliente (Nesto#458): el usuario no veía nada raro (el combo cargaba igual); solo
        cambia el ruido de ELMAH.
      - El log temporal de las notificaciones de Redsys (NestoAPI#445): diagnóstico interno.

    Ejecutar en SSMS contra NV DESPUÉS de publicar.
*/

SET NOCOUNT ON;
USE NV;

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
VALUES
('1.10.25.1', '2026-09-02', 'Corregido', 'La plantilla ya no conserva un descuento de rebajas que ha caducado',
 'Si un borrador de la plantilla se guardo con una campana activa (por ejemplo las rebajas de verano) y la campana se borra antes de mandar el pedido, la plantilla se quedaba con aquel descuento como si lo hubiera tecleado el vendedor, y el pedido se rechazaba con "no se encuentra autorizado el descuento del 30 %" sin que nadie lo hubiera puesto. Ahora, al abrir el borrador, los descuentos que puso Nesto se vuelven a calcular y siguen a la campana (bajan si se acabo, suben si empieza otra). Los descuentos tecleados a mano por encima del calculado se conservan como siempre.', 'Nesto', 1, 'sa');

SELECT Version, Titulo FROM Novedades WHERE Version = '1.10.25.1';
