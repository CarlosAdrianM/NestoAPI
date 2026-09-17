/*
    Novedades de la versión 1.10.28.2 (17/09/2026).

    Tanda completa: NestoAPI + Nesto (ClickOnce 1.10.28.2). NO se sube la tercera cifra: son correcciones
    y mejoras menores sobre la 1.10.28.0 de ayer (la única funcionalidad nueva visible es el botón "Ver
    factura" del Extracto Cliente).

    ORDEN: 1) publicar NestoAPI; 2) publicar la ClickOnce; 3) ejecutar Scripts/Issue489_NovedadesAmbitoApp.sql;
    4) ejecutar ESTE script. Los scripts de #490 (nombres restaurados) y #487 (SP de impagados) YA se
    ejecutaron el 17/09. Scripts/Issue493_ActivarCTT.sql NO se ejecuta: es para el día D de CTT.

    SE OMITE A PROPÓSITO (el usuario no lo nota):
      - NestoAPI#490: el API ya no procesa sus propios mensajes del bus (causa de los nombres en minúsculas).
      - NestoAPI#493: integración con CTT Express (sigue como agencia sombra hasta el día D).
      - NestoAPI#489/#495: las novedades de NestoApp y de NestoAPI salen de la misma tabla.
      - NestoAPI#492: el extracto sabe si el documento es una factura (es lo que habilita el botón).
      - NestoAPI#457 (corte 1): endpoint de ofertas sugeridas; todavía sin pantalla.
      - Nesto#340: saldo de reembolsos por API, 13 clases más en ObservableObject.
      - NestoAPI#74: el job de correos post-compra pasa a los jueves a las 05:00 (mismo sábado de envío).
      - Tests, scripts, issues de NestoApp.

    Ejecutar en SSMS contra NV. Los literales de texto van con N'...': las columnas son nvarchar y sin
    el prefijo cualquier carácter fuera de Windows-1252 se graba como '?'.
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.28.2'; -- <-- AJUSTAR si se publica otra versión
DECLARE @fecha DATE = '2026-09-17';

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
VALUES
(@version, @fecha, 'Nuevo', N'En el Extracto Cliente se abre la factura desde el movimiento',
 N'En la ventana Extracto Cliente, haciendo doble clic en un movimiento cuyo número de documento es una factura, o con el botón "Ver factura", se abre el PDF de esa factura. El botón solo se activa cuando el documento seleccionado existe como factura; en los demás movimientos (recibos, pagos, abonos sin factura) queda desactivado.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Corregido', N'Ya se puede pasar un pedido a "Ahora lo que hay, el resto de una vez" aunque haya regalos sin stock',
 N'Al cambiar el modo de entrega a "Ahora lo que hay, el resto de una vez" o a "Tras reponer de tiendas", Nesto denegaba el cambio si el pedido tenía muestras o regalos sin stock, con el mismo aviso que al desmarcar "Servir junto". Esa comprobación solo tiene sentido en "Según vaya entrando" (el único modo en el que el regalo podría salir después de lo que lo justifica), y ahora solo se aplica ahí. El aviso, cuando sale, ofrece los otros dos modos como alternativa.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Corregido', N'El correo del pedido no avisa en rojo cuando el modo es "Tras reponer de tiendas"',
 N'El correo de "Pedido nuevo" o "Pedido modificado" mostraba el aviso rojo "¡¡¡ ATENCIÓN !!! Modo de entrega: Tras reponer de tiendas", como si fuera un problema. Ese modo es una forma correcta de servir (es el modo por defecto desde ayer). El aviso en rojo queda solo para los modos que pueden salir en varias entregas sin esperar a la reposición.', 'NestoAPI', 1, 'sa'),

(@version, @fecha, 'Corregido', N'Los nombres de producto que aparecían en minúsculas vuelven a estar en mayúsculas',
 N'Tras la versión de ayer, 676 fichas de producto mostraban el nombre en formato oración ("Champú hidratante") en vez de en mayúsculas. El cambio de formato era solo para la tienda online (por el posicionamiento en buscadores), pero el API procesó sus propios mensajes de sincronización y reescribió las fichas de Nesto. Los 676 nombres se han restaurado en mayúsculas y el API ya no vuelve a procesar lo que él mismo publica. La tienda sigue mostrando los nombres en formato oración.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Mejorado', N'Los portes provinciales pasan a 5 € en los pedidos con fecha 1 de octubre o posterior',
 N'Los portes de los pedidos provinciales que no llegan al mínimo pasan de 3,50 € a 5 €. El cambio ya está preparado y se aplica solo por la fecha del pedido: los pedidos con fecha anterior al 1 de octubre siguen a 3,50 € aunque se sirvan o facturen después, y los que tengan fecha 1 de octubre o posterior van a 5 €. El importe mínimo para no cobrar portes no cambia.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Corregido', N'La comisión de un impagado que cae en medio céntimo exacto se redondea como el banco',
 N'Al contabilizar el fichero de impagados SEPA, cuando la comisión salía en medio céntimo exacto (por ejemplo, un impagado de 100,50 € da una comisión de 1,005 €), Nesto la redondeaba hacia arriba (1,01 €) y el banco al par (1,00 €), con lo que el apunte descuadraba un céntimo con el cargo real. Ahora se redondea igual que el banco.', 'Nesto', 1, 'sa');
