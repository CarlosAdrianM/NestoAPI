/*
    Novedades de la versión 1.10.63.0 (17/11/2026): API + Nesto.

    ORDEN:
      1) Scripts (sa): ya lanzados hoy (Issue874_ClientesDireccion2.sql, Issue868_AvisosFichaFotoMozo.sql,
         OneShot_20261117_ReenviarProductosConStock.sql, OneShot_20261117_OrigenCambioFormaFiscal.sql,
         OneShot_20261117_Embudo_SacarClientesVivos.sql). No hay ninguno nuevo.
      2) Publicar la API (master). 3) Publicar Nesto (1.10.63.0). 4) ESTE script.

    NO van aquí porque son SUGERENCIAS/INCIDENCIAS que se contestan en su propio hilo al publicar:
      1302 (prepago y saldo a favor), 1308 (producto que no llegaba a la tienda), 1314 (nota de recogida: los dos fallos y el
      mensaje que dice qué falta), 1317 (cobrar antes de imprimir en tienda: va también aquí como novedad por ser para todas las tiendas).

    Se omiten a propósito (nada perceptible o interno):
      - Embudo: solo clientes dormidos; ajuste automático del ritmo apagado.
      - Reenvío nocturno único de productos a la tienda.
      - Ficha de cliente partida en dos ViewModels (Nesto#646): no cambia nada a la vista.
      - API: ?devolverPedido=true, origen «Cambio de forma fiscal» (se ve solo en el desplegable), la Dirección 2 en el envío.

    Ejecutar contra NV como sa. Idempotente (compara por versión y título). Los literales van con N'...'.
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.63.0';
DECLARE @fecha DATE = '2026-11-17';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000), Perfiles nvarchar(200));

INSERT INTO @novedades (Categoria, Titulo, Descripcion, Perfiles) VALUES
    ('Nuevo', N'Pedidos: aviso si otra persona lo cambia mientras usted lo tiene abierto',
     N'Si otra persona guarda un pedido que usted tiene abierto, aparece una banda en el pedido que lo dice, con quién y a qué hora. Si aun así intenta guardar con la copia antigua, Nesto no lo deja (antes se perdía lo que había hecho el otro, por ejemplo un prepago) y le ofrece dos salidas: «Recargar», que trae el pedido como está ahora, o «Recargar conservando mis cambios», que lo trae y vuelve a poner encima lo que usted había cambiado. Así no hay que repetir el trabajo.',
     NULL),
    ('Nuevo', N'Alta de cliente: «¿Qué es?» y el número de la calle',
     N'Al dar de alta un cliente, debajo de la dirección se pregunta «¿Qué es?»: piso (con piso y puerta), local (con el rótulo que se ve desde la calle), casa, nave u otro. Es lo que necesita el repartidor para encontrarlo y va en la etiqueta del envío. Además, si la dirección de Google viene sin número, se escribe en la casilla «Número» (solo el portal: «12», «12 B» o «S/N») y Nesto lo comprueba; si Google no lo encuentra, pregunta si es correcto antes de seguir.',
     NULL),
    ('Nuevo', N'Facturar en tienda: primero se cobra y después se imprime',
     N'Al facturar un pedido en la tienda se abre primero la ventana de cobro, con esa factura ya marcada, y la factura se imprime después, ya cobrada (antes salía con «Pendiente de pago»). Si el cobro es en efectivo, se escribe lo que entrega el cliente y Nesto calcula el cambio.',
     NULL),
    ('Mejorado', N'«Iniciar Pedido Nuevo» está ahora arriba',
     N'En «Pedidos de venta», el botón «Iniciar Pedido Nuevo» ha pasado de la esquina de abajo a la parte de arriba de la lista, encima del buscador de pedidos, donde se ve.',
     NULL),
    ('Mejorado', N'Ficha de cliente: cuál es la cuenta de los recibos y cómo cambiarla',
     N'En la ficha del cliente, en las cuentas bancarias, ahora se lee cuál es la cuenta del cliente («Cuenta del cliente: 2») y hay un botón «Usar esta cuenta para los recibos» para cambiarla por la que esté seleccionada. Además, al pasar de un cliente a otro ya no se queda en pantalla la cuenta del anterior.',
     NULL),
    ('Mejorado', N'Propuestas de stock: solo Dirección, y máximos mejor calculados',
     N'La ventana «Propuestas de stock» queda solo para Dirección; «Cambiar estado de productos» sigue disponible para Compras. Y en los productos de proveedores que tardan mucho en servir, el máximo propuesto ya no sale igual que el mínimo.',
     N'Compras,Dirección'),
    ('Mejorado', N'Cambiar un efecto del extracto: solo Administración y Dirección',
     N'Cambiar la forma de pago, el vencimiento o la cuenta de un efecto desde la ficha del cliente queda reservado a Administración, Dirección e Informática. A los demás, Nesto les dice que no tienen permiso.',
     NULL),
    ('Corregido', N'Ficha de producto: máximos y mínimos de otro producto',
     N'Al cambiar deprisa de un producto a otro en la ficha, podían quedar en pantalla el stock máximo y mínimo del producto anterior junto a la referencia del nuevo. Ya no pasa: se ven siempre los del producto que se está mirando.',
     NULL),
    ('Corregido', N'Buscar pantalla (Ctrl+K): el cursor ya no salta al segundo campo',
     N'Al abrir una pantalla con Ctrl+K e Intro, el cursor acababa en el campo siguiente al primero (por ejemplo, en la ficha de producto). Ahora se queda donde debe.',
     NULL);

INSERT INTO dbo.Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa', n.Perfiles
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM dbo.Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesInsertadasAhora;
SELECT Id, Categoria, Titulo FROM dbo.Novedades WHERE Version = @version ORDER BY Id;
