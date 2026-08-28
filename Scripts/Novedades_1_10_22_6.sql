/*
    Novedades de la versión 1.10.22.6.

    ############################################################################
    #  NO EJECUTAR EN EL DESPLIEGUE DE API DEL 28/08/2026.                     #
    #                                                                          #
    #  La versión ClickOnce publicada hoy es la 1.10.22.5, y estas entradas     #
    #  son de la 1.10.22.6. Si se insertan antes de publicar Nesto 1.10.22.6,   #
    #  el popup de novedades reaparece en CADA actualización hasta que la       #
    #  versión real alcance a la de las entradas (regla de Nesto#372).          #
    #                                                                          #
    #  Ejecutar cuando se publique el ClickOnce 1.10.22.6, junto con las        #
    #  novedades que traiga esa publicación.                                    #
    ############################################################################

    SE OMITE A PROPÓSITO:
      - El arreglo va acompañado de tests y de una subconsulta en vez de un join, detalles
        internos que no aportan nada al usuario.
*/

SET NOCOUNT ON;
USE NV;

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
VALUES
('1.10.22.6', '2026-08-28', 'Corregido', 'La hoja del almacen ya no se bloquea en los pedidos con cupon de descuento',
 'Desde el dia 25 el sistema revisa, antes de imprimir la hoja del almacen, que ninguna linea tenga reservadas mas unidades de las pedidas, para que no se sirva de mas sin que nadie lo note. Esa revision se equivocaba con las lineas de cantidad negativa (el cupon de descuento y las devoluciones): las tomaba por descuadradas y no dejaba sacar la hoja, aunque el pedido estuviera perfecto. Ya no las tiene en cuenta, y el aviso sigue saltando cuando el descuadre es de verdad.', 'NestoAPI', 1, 'sa');

SELECT @@ROWCOUNT AS Insertadas;
SELECT Id, Version, Categoria, Titulo FROM Novedades WHERE Version = '1.10.22.6' ORDER BY Id;
