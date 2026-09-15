/*
    Novedades de la versión 1.10.27.0 (15/09/2026).

    Tanda completa: NestoAPI + Nesto (ClickOnce 1.10.27.0). Se sube la tercera cifra porque hay
    funcionalidad nueva visible: la pestaña Retrasados en Agencias y la casilla de pausar una
    casa en la tienda.

    ORDEN: 1) SQL como sa (Issue482_CabPedidoVta_ModoServicio, ya ejecutado); 2) publicar
    NestoAPI; 3) publicar la ClickOnce; 4) ejecutar ESTE script.

    SE OMITE A PROPÓSITO (el usuario no lo nota):
      - NestoAPI#482 slice 1 (modo de servicio del pedido en la API): sin selector en Nesto ni en
        la app todavía; se anunciará cuando se pueda elegir. Hoy todo se comporta como antes.
      - Nesto#340: CargarListaEnviosPedido por API (Agencias A3) y los módulos Producto,
        PedidoCompra y Cliente a ObservableObject (modernización).
      - NestoAPI#481 punto 3: el usuario de auditoría de inventarios y vendedores por grupo de
        producto ya no se graba como la cuenta del servidor. Solo se ve en la BD.
      - Tests, EDMX, scripts, prompts al equipo del módulo de PrestaShop.

    Ejecutar en SSMS contra NV. Los literales de texto van con N'...': las columnas son nvarchar y sin
    el prefijo cualquier carácter fuera de Windows-1252 (una flecha, unas comillas tipográficas) se graba como '?'.
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.27.0'; -- <-- AJUSTAR si se publica otra versión
DECLARE @fecha DATE = '2026-09-15';

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
VALUES
(@version, @fecha, 'Nuevo', N'Pestaña "Retrasados" en Agencias: envíos que la agencia no da por entregados',
 N'Al lado de Incidentados hay una pestaña nueva con los envíos que salieron hace más de N días (4 por defecto) y de los que la agencia todavía no ha confirmado la entrega. Se ve el último evento que contó la agencia ("REPARTO", "DOCUMENTADO"...), los días que lleva, y las filas se colorean: ámbar de 6 a 10 días, rojo a partir de 11. Se puede filtrar por vendedor y por la agencia seleccionada. Al elegir una fila funcionan "Abrir seguimiento" (o doble clic), "Actualizar estado" y el menú de copiar nº de pedido / envío; si al actualizar resulta entregado, desaparece de la lista. Sirve para llamar al cliente antes de que llame él: un envío estuvo 19 días "en reparto" sin que nadie lo viera.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Nuevo', N'Pausar la venta de una casa entera en la tienda desde el mantenimiento de familias',
 N'En Herramientas > Mantenimiento de familias hay una casilla nueva, "Venta pausada en tienda". Al marcarla, todos los productos de esa familia se retiran de la tienda online (por ejemplo, por una tarifa del proveedor errónea, como pasó con Mirplay); al desmarcarla vuelven SOLO los que se pausaron por aquí. Hasta ahora había que pedírselo por correo al equipo de la tienda con la lista de referencias. La tienda empezará a obedecer la casilla cuando se instale la nueva versión del módulo de sincronización; mientras tanto, marcarla no hace daño.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Corregido', N'El aviso "ha cogido picking" con importe mostraba el importe de la línea entera cuando salía solo una parte',
 N'En los pedidos con la casilla "Avisar con importe cuando coja picking", si de una línea de 15 unidades salían 2, el correo decía cantidad 2 pero el importe de las 15 (pedido 925872: 176,41 € cuando lo que salía eran 53,79 €). Ahora el importe es el de lo que sale de verdad. Solo afectaba al correo; el albarán y el stock estaban bien.', 'NestoAPI', 1, 'sa'),

(@version, @fecha, 'Corregido', N'Los correos post-compra ya no saludan con el nombre de la empresa',
 N'El sábado 12 salieron saludos como "Hola The Look Getafe SL". Cuando el nombre del cliente es una sociedad (S.L., S.A., SRL...) o no es un nombre de persona, el saludo es genérico ("Hola"). Además, los nombres se procesan en grupos más pequeños, que es donde empezó a fallar.', 'NestoAPI', 1, 'sa'),

(@version, @fecha, 'Corregido', N'Dejan de llegar los avisos de "NIF incorrecto" del cliente 9500 PEDIDO TIENDA',
 N'El 9500 es un cliente ficticio de venta en tienda (NIF "00") cuyas facturas son simplificadas, como el 10458 VENTA TIENDA. El circuito de comprobación de NIF lo trataba como un cliente real y mandaba un aviso en cada pedido. Ya no.', 'NestoAPI', 1, 'sa');
