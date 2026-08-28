/*
    Novedades de la versión 1.10.23.2 (28/08/2026, segunda publicación del día).

    Una sola entrada, y no es de esta tanda: la mejora de la tramitación salió a mediodía en la
    1.10.23.1 y se quedó sin contar. Se cuenta ahora, que es justo lo que le pasó hoy al almacén.

    SE OMITE TODO LO DEMÁS A PROPÓSITO (nada de esto lo nota el usuario):
      - El registro en ELMAH de los errores de la ventana de Agencias que antes se quedaban solo
        en el aviso de pantalla. Es para que informática se entere; el usuario ve lo mismo.
      - GLS contesta el mismo error de "ya existe" de dos maneras; ahora se reconocen las dos.
        Es un remate del arreglo que ya se contó en la 1.10.23.0.
      - #340 (A4.1): la consulta del envío a tramitar por la API. Solo afecta al piloto, que hoy
        es un único usuario.
      - #422: un solo constructor del mensaje de productos, en vez de cinco copias. Refactor.
      - El jefe de ventas y su correo dejan de estar escritos a mano en el código: salen de
        EquiposVenta y de la ficha del vendedor. Hoy manda exactamente los mismos correos.

    Ejecutar en SSMS contra NV DESPUÉS de publicar.
*/

SET NOCOUNT ON;
USE NV;

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
VALUES
('1.10.23.2', '2026-08-28', 'Corregido', 'Si la agencia dice que el envio ya estaba, Nesto lo cierra igualmente',
 'Al tramitar un envio con GLS podia pasar que la agencia ya lo tuviera registrado de un intento anterior. Antes eso se trataba como un error: la agencia decia "ya existe el codigo de barras", el envio se quedaba sin cerrar en Nesto y no habia manera de terminarlo por mucho que se reintentara, habia que avisar a informatica. Ahora Nesto entiende que si la agencia ya lo tiene es que esta hecho, y lo cierra con normalidad, contabilizando su reembolso si lo lleva. Los errores de verdad de la agencia siguen avisando como siempre.', 'Nesto', 1, 'sa');

SELECT @@ROWCOUNT AS Insertadas;
SELECT Id, Version, Categoria, Titulo FROM Novedades WHERE Version = '1.10.23.2' ORDER BY Id;
