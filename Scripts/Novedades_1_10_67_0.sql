/*
    Novedades de la versión 1.10.67.0 (25/11/2026): API + Nesto. Va con Ariadna 2.43, 2.44 y 2.45 (Scripts/Novedades_Ariadna_2_43.sql,
    Novedades_Ariadna_2_44.sql y Novedades_Ariadna_2_45.sql).

    ORDEN:
      1) Scripts (sa), ANTES de publicar la API: Scripts/Issue901_Proyectos.sql, Scripts/Issue857i_ComprasOrigenesAgencia.sql,
         Scripts/Issue906_DiferenciasReposicionDiscusiones.sql y Scripts/OfertasClienteMostradas.sql (sin ellos, cada cosa sigue como antes
         o da 503: Proyectos dice «Los proyectos todavía no están activados en el servidor.»).
      2) Publicar la API (master). 3) Publicar Nesto (1.10.67.0). 4) ESTE script.
      La lectura obligatoria de la etiqueta de las muestras en el picking nace APAGADA en los tres almacenes (no hace falta ningún script).

    NO van aquí porque son SUGERENCIAS/INCIDENCIAS que se contestan (o se pasan a Implementada) en su propio hilo al publicar:
      1432 (borradores de la plantilla que se actualizan en vez de duplicarse, con su casilla), 1433 (diferencias de reposición «En discusión»;
      además sin pantalla en Nesto), 1455 (enviar una reposición con inventario pendiente dice qué fila es), 1456 («Borrar» en la lista
      «Completo»: va dentro de la novedad de las pendientes de días anteriores, que es más amplia). La 1460 (pedido de tienda con muestras
      sin stock) se contesta en su hilo Y ADEMÁS va aquí como novedad para Vendedores y Tiendas (Carlos 25/11/26: afecta a todas las tiendas).

    Se omiten a propósito (nada perceptible en Nesto, sin pantalla todavía o ya contado):
      - «Ofertas para ti», su registro en sombra, la push de campaña sin carrito, el lector y el refresco del token: son de la app de clientes.
      - Pérdida desconocida por almacén (#893) y abonar solo lo comprado (#891, en sombra): sin pantalla en Nesto.
      - Lectura obligatoria de la etiqueta de las muestras en el picking: va en la novedad de Ariadna 2.45 (en Nesto no hay pantalla).
      - Deshacer un abono por el camino de un pedido nuevo deja rastro (#907): ya lo dice la novedad de la 1.10.66.3 («no se puede deshacer dos veces»).
      - Los cambios de los pedidos sin IVA (líneas a 0 % al quitar el IVA; sus documentos no se descargan): sin novedad, por decisión de Carlos (25/11/26).
      - ELMAH sin credenciales, impresora de etiquetas por almacén (solo se nota en la ventana de Muestras, donde ya se cuenta).

    Ejecutar contra NV como sa. Idempotente (compara por versión y título). Los literales van con N'...'.
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.67.0';
DECLARE @fecha DATE = '2026-11-25';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000), Perfiles nvarchar(200));

INSERT INTO @novedades (Categoria, Titulo, Descripcion, Perfiles) VALUES
    ('Nuevo', N'Inmovilizados: buscar una máquina e imprimir su etiqueta',
     N'Ventana nueva en Contabilidad › Inmovilizados. Busque por número («I/1823» o 1823) o por descripción, y filtre por delegación. «Imprimir etiqueta» saca de 1 a 10 copias de la etiqueta de la máquina: «INMOVILIZADO · NO ES STOCK», «I/1823» en grande, la descripción con el número de serie entero y un código de barras. Sale en la impresora de etiquetas de su almacén y, al terminar, Nesto dice adónde ha ido cada una y por qué no ha salido alguna. Si alguien lee esa etiqueta al hacer inventario o al buscar un producto, se le avisa de que es un inmovilizado y no se cuenta como stock. En el detalle del pedido, escribir o leer «I/1823» en «Producto» pone la línea del inmovilizado 1823.',
     N'Almacén,Tiendas,Administración'),
    ('Nuevo', N'Proyectos: la cuenta de explotación de cada curso, feria o campaña',
     N'Ventana nueva en Contabilidad › Proyectos, para Administración y Dirección. Dé de alta cada proyecto (una convocatoria de un curso del SEPE, una feria, un evento, una campaña) y, en «Apuntes por imputar», reparta entre ellos los apuntes ya contabilizados de gastos e ingresos (cuentas 6 y 7): entero a un proyecto, a partes iguales o por importes. La contabilidad no cambia. En «Cuenta de explotación» verá los ingresos, los gastos y el resultado de cada proyecto, el presupuesto frente a lo real y la comparación con otros proyectos del mismo curso, y puede sacarla a Excel. Cerrar y reabrir un proyecto lo hace Dirección.',
     N'Administración'),
    ('Nuevo', N'Muestras: una etiqueta en el cajón o la gaveta, y la hoja con fotos',
     N'Las muestras que no traen código de barras del fabricante llevan ahora UNA etiqueta por referencia en el cajón del mostrador (Reina y Alcobendas) o en la gaveta o el hueco (Algete), no en cada unidad. Ventana nueva en Productos › Almacén › «Muestras»: elija el almacén, la marca si quiere y «Solo las que tienen stock», y pulse «Ver». Marque las que quiera e «Imprimir etiquetas» (sale en la impresora del almacén, o en la que elija); a las que aún no tienen código se les da uno al imprimir. «Hoja en PDF» saca, por marca, la foto, el nombre, la referencia y el código de barras de cada muestra. Antes de imprimir, Nesto dice cuántas van a salir y pide confirmación. «Dar código a las muestras sin código» lo pueden usar Almacén, Compras y Dirección.',
     N'Almacén,Tiendas,Compras'),
    ('Nuevo', N'Facturas a organismos públicos: «Descargar para FACe (Facturae)»',
     N'En el detalle del pedido (menú de «Imprimir Factura») y en la pestaña Facturas de la ficha del cliente, «Descargar para FACe…» comprueba la factura y la descarga en formato Facturae, lista para importarla en B2Brouter, que la firma y la envía a FACe. La ventana enseña en rojo lo que falta y en amarillo los avisos, deja escribir los códigos DIR3 del organismo, su número de pedido, el expediente y el contrato, y explica los pasos en B2Brouter. Si el organismo no da número de pedido, se pone el nuestro. Los códigos DIR3 se guardan en la ficha del cliente, pestaña «Direcciones», sección «Factura electrónica (FACe)».',
     N'Administración'),
    ('Mejorado', N'Inventario de la tienda: rellenarlo pide confirmación y se puede deshacer',
     N'Antes de rellenar el inventario de una tienda, Nesto dice cuántas filas va a crear («Va a crear 3.032 filas de inventario en Alcobendas…») y hay que escribir el nombre del almacén para que se active «Rellenar». Si se ha rellenado por error, «Deshacer el rellenado» borra esas filas, siempre que sean todas suyas y no haya contado ninguna todavía; si no, pídaselo a Dirección.',
     N'Tiendas'),
    ('Mejorado', N'Inventario de la tienda: las filas pendientes de otros días, en la lista «Completo»',
     N'La lista «Completo» enseña al final, en cursiva y con «pendiente del 24/11» en la columna «Día», las filas que se quedaron sin contabilizar otro día, y avisa de cuántas son. Las que se pueden borrar llevan su botón «Borrar», con la misma confirmación que en las comprobaciones.',
     N'Tiendas'),
    ('Mejorado', N'Eventos online: no se pasa lista',
     N'En un evento online ya no salen la casilla «Vino» ni «Guardar lista»: al día siguiente, todas las inscritas que han pagado se dan por asistidas (las que dijeron «No podré ir», no). Y en cualquier evento, si guarda la lista sin marcar a nadie, Nesto le pregunta antes, porque las inscritas que han pagado perderían el cupón.',
     N'Tiendas,Administración'),
    ('Mejorado', N'Descuentos: queda autorizado lo que está cargado para el cliente',
     N'Al guardar un pedido, un descuento se da por autorizado solo si está cargado para ese cliente o ese producto tal como Nesto lo aplica al calcular el precio. Por ejemplo, un descuento cargado solo para los productos outlet de una marca ya no autoriza ese descuento en el resto de la marca. Si a un cliente se le ha pactado un descuento y al guardar sale el aviso de que no está autorizado, es que no está cargado: pida la autorización como siempre y que se lo carguen. Además, que un producto esté en una campaña ya no impide el descuento que el cliente tiene pactado para esa marca o ese grupo de productos: vale el mayor.',
     N'Vendedores,Tiendas,Administración'),
    ('Mejorado', N'Cupón o cheque regalo añadido a un pedido en preparación: sale en la misma entrega',
     N'Si añade un cupón de un evento o un cheque regalo a un pedido que ya tiene el picking sacado, el descuento va en el mismo albarán y la misma factura que la mercancía, siempre que esa entrega llegue al mínimo del cupón o del cheque. Si no llega, o la agencia ya se ha llevado el pedido, queda para la siguiente entrega, como antes.',
     N'Vendedores,Tiendas,Almacén'),
    ('Mejorado', N'Envíos de proveedor por asignar: bultos, kilos y pedidos «de pendientes»',
     N'En Compras › Envíos de proveedor por asignar, el envío enseña los bultos y los kilos cuando la agencia los dice (hoy DHL: «8 bultos · 123 kg»), y el candidato que es un pedido de pendientes lo indica debajo, en gris («de pendientes: se envió el 20/08/2026, hace más de 30 días»); se puede asignar igual. El aviso de la campana llega a los 20 minutos de recibir el correo, sin esperar a la siguiente revisión, y el correo reenviado a Compras dice dónde asignarlo. Ya no salen los envíos que ha resuelto otro correo, los que han llegado ni los que ya tienen albarán.',
     N'Compras'),
    ('Mejorado', N'Correos de seguimiento: más envíos van solos al pedido correcto',
     N'En los proveedores con control de pendientes, el envío se asigna al pedido en curso más antiguo y nunca a uno de pendientes (el correo dice por qué). También se reconoce el proveedor por el nombre del remitente que da la agencia y, en DB Schenker, por la delegación de origen una vez asignado un envío de allí. Y en el correo de proveedores con la fecha pasada, si la agencia ya tiene el envío en camino, la nueva fecha es la de su llegada, se dice qué agencia lo lleva y no se marca «Hay que llamar al proveedor».',
     N'Compras'),
    ('Mejorado', N'Pedido de tienda: las muestras sin stock se quitan solas',
     N'En un pedido de Reina o Alcobendas, las muestras que no hay en la tienda se quitan solas al guardar (o se deja solo lo que hay) y Nesto lo avisa, en vez de rechazar el pedido. La plantilla ya no vuelve a «Todo junto», que en tienda no vale, y si algo no se puede quitar dice qué hacer: borrarlo o pasar el pedido desde Algete. Si el pedido solo lleva muestras, no se quitan. Lo avisó Sancho.',
     N'Vendedores,Tiendas');

INSERT INTO dbo.Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa', n.Perfiles
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM dbo.Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesInsertadasAhora;
SELECT Id, Categoria, Titulo, Perfiles FROM dbo.Novedades WHERE Version = @version ORDER BY Id;
