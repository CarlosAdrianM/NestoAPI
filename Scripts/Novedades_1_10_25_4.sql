/*
    Novedades de la versión 1.10.25.4 (04/09/2026).

    Solo sube la última cifra: son correcciones. Todas giran alrededor del mismo caso real, el
    pedido 925368, cuyo PDF llevaba dos días sin poder generarse por un céntimo.

    SE OMITE TODO LO DEMÁS A PROPÓSITO (nada de esto lo nota el usuario de Nesto):
      - Cobro con tarjeta guardada en la app de clientes: se retiró la marca MIT (el pedido que
        hace el propio cliente es CIT, NestoAPI#181) y se montó la autenticación EMV 3DS 2 por
        REST para que no vea la pasarela. Es de la app, no de Nesto.
      - El usuario de los prepagos ya no es la cuenta de máquina del pool (NestoAPI#456): se
        arregla de aquí en adelante y solo se ve consultando la tabla.
      - Buscador de clientes por índice (NestoAPI#455): va detrás de un parámetro APAGADO, no
        cambia nada hasta que se encienda.
      - Los fallos al generar un PDF de factura ya quedan registrados en ELMAH, y se retiró el
        diagnóstico temporal de Redsys (#445): diagnóstico interno.
      - Retirado el camino antiguo de tramitar envíos por Entity Framework (Nesto#340) y los
        envoltorios de Caja salen de Prism: refactor interno, se comporta igual.

    Ejecutar en SSMS contra NV DESPUÉS de publicar.
*/

SET NOCOUNT ON;
USE NV;

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
VALUES
('1.10.25.4', '2026-09-04', 'Corregido', 'Ya se puede sacar el PDF de un pedido cuyos vencimientos no cuadraban por un céntimo',
 'Cuando un descuento dejaba medio céntimo (por ejemplo 63,50 € con un 15 %), el total que se usaba para repartir los vencimientos no era exactamente el del pedido. Los tres vencimientos sumaban un céntimo de más, la proforma se negaba a generarse y, si se corregía el vencimiento a mano, al guardar volvía a descuadrarse solo. Ahora el importe se calcula igual en todas partes, así que los vencimientos cuadran desde el principio y el PDF sale. Los pedidos que ya estaban descuadrados se arreglan con solo abrirlos y darle a modificar una vez.', 'Nesto', 1, 'sa'),

('1.10.25.4', '2026-09-04', 'Corregido', 'Cuando falla la descarga de un PDF, Nesto dice por qué',
 'Si el servidor no podía generar el PDF de un pedido o de una factura, salía el mensaje "value cannot be null. (Parameter buffer)", que no decía nada y obligaba a llamar. Ahora aparece el motivo real, por ejemplo que los vencimientos no cuadran con el total de la factura.', 'Nesto', 1, 'sa'),

('1.10.25.4', '2026-09-04', 'Corregido', 'Al descargar varias facturas de un cliente, ya no se salta en silencio las que fallan',
 'Al descargar varias facturas a la vez, la que daba error se quedaba sin descargar sin avisar: no había ni PDF ni mensaje, y la carpeta se abría igual como si todo hubiera ido bien. Ahora las demás se descargan como siempre y al terminar se indica cuáles no han salido y por qué.', 'Nesto', 1, 'sa');

SELECT Version, Categoria, Titulo FROM Novedades WHERE Version = '1.10.25.4';
