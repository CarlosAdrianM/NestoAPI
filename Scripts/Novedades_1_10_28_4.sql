/*
    Novedades de la versión 1.10.28.4 (21/09/2026).

    Tanda completa: NestoAPI + Nesto (ClickOnce 1.10.28.4). NO se sube la tercera cifra: lo del día son
    correcciones y una familia nueva con reglas propias; en Nesto solo cambia a quién se le ofrece forzar
    un pedido. Lo gordo del día (CTT Express en producción) ya está activo desde esta tarde y no depende
    de esta publicación, pero se cuenta aquí porque es lo que el almacén va a notar mañana.

    ORDEN:
      1) Publicar NestoAPI.
      2) REINDEXAR EL BUSCADOR: POST http://api.nuevavision.es/api/buscador/indexar (o esperar al job).
         Hasta que el índice se regenere, los productos de familias restringidas SIGUEN saliendo en las
         búsquedas, porque el campo nuevo no existe en los documentos viejos.
      3) Publicar la ClickOnce.
      4) ESTE script.

    YA EJECUTADO HOY (no hay que repetirlo):
      - Scripts/Issue500_ClientePrincipalDuplicado.sql (10:23).
      - Scripts/Issue501_FamiliasRestringidas.sql (13:35).
      - Scripts/Issue493_ActivarCTT.sql (13:38) y las credenciales de producción de CTT en el servidor.

    SE OMITE A PROPÓSITO (el usuario no lo nota):
      - NestoAPI#500: punto único para buscar la ficha principal del cliente y una consulta muerta menos
        en formas de pago.
      - NestoAPI#501: el permiso propio de las familias restringidas y el campo del índice del buscador.
      - NestoAPI#493: que secretos.config deje de llevar el sandbox de CTT (infraestructura).
      - Tests, scripts, issues.

    Ejecutar en SSMS contra NV. Los literales de texto van con N'...': las columnas son nvarchar y sin
    el prefijo cualquier carácter fuera de Windows-1252 se graba como '?'.
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.28.4'; -- <-- AJUSTAR si se publica otra versión
DECLARE @fecha DATE = '2026-09-21';

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
VALUES
(@version, @fecha, 'Nuevo', N'CTT Express empieza a llevar paquetes',
 N'Desde hoy CTT Express es una agencia más: aparece en la ventana de Agencias y el comparador la elige cuando es la más barata. Arranca poco a poco, como pidió la propia CTT: de momento solo compite en envíos de la provincia de Madrid; el resto sigue saliendo como hasta ahora. Sus etiquetas salen por la tercera Zebra, la que usaba Sending, con rollos blancos de 100x150. El seguimiento se consulta igual que el de las demás.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Nuevo', N'Kinetics: solo la ofrecen los vendedores presenciales',
 N'La familia Kinetics no se vende por la tienda online ni aparece en el buscador, la plantilla o la ficha de producto salvo que quien esté mirando sea un vendedor presencial. Además no se puede vender a un cliente que haya comprado Faby o Greenik en los dos últimos años: al guardar el pedido avisa de que ese cliente ya compró la otra marca y de cuándo lo hizo. Es una regla general, no solo de Kinetics: se pueden configurar más familias y más incompatibilidades sin tocar el programa.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Mejorado', N'Quien tiene permiso puede forzar un pedido aunque no esté en Dirección o Almacén',
 N'Cuando el pedido no pasa la validación, Nesto ofrecía «¿Desea crearlo de todos modos?» solo a quien estuviera en los grupos de Dirección, Almacén o Tiendas. Ahora también se le ofrece a quien tenga el permiso dado en sus parámetros de usuario, que es como se conceden las excepciones puntuales. El servidor ya lo permitía; era Nesto el que ni siquiera llegaba a preguntar.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Corregido', N'Un cliente con dos fichas principales ya no rompe la plantilla ni la factura',
 N'Algunos clientes tenían dos fichas marcadas como principal, y eso daba un error al abrir la plantilla de venta, al consultar sus plazos y formas de pago, al ver su extracto o al generar la factura. Se han corregido las 77 fichas afectadas y, además, el programa ya no se rompe si vuelve a aparecer un caso así: coge la primera ficha y sigue.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Corregido', N'Las remesas ya no se llevan recibos de cuentas bancarias dadas de baja',
 N'Al crear una remesa se podía girar un recibo contra una ficha bancaria del cliente que estaba dada de baja, con lo que el recibo acababa devuelto. Ahora esos efectos quedan retenidos con el motivo, igual que los de IBAN incorrecto, y no se pueden forzar: si de verdad hay que cobrar por esa cuenta, primero hay que cambiarle el estado a la ficha.', 'Nesto', 1, 'sa');
