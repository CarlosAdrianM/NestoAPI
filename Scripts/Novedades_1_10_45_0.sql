/*
    Novedades de la versión 1.10.45.0 de Nesto (20/10/2026).
    Versión de la ClickOnce: 1.10.45.0 (pubxml 1.10.45.* en revisión 0: sugerencias de productos en la plantilla, sugerencias
    privadas, Enviar reposición que no pierde cambios, fecha permitida en la conciliación, avisos de enlaces de pago en Agencias).
    Ariadna NO entra aquí: la 2.14 tiene su propio script (Novedades_Ariadna_2_14.sql).

    Tanda: NestoAPI (#666 correo de incidencias con copia a logística, #660 correo «Gracias por su pedido», #658 informe de
    tienda [sin pantalla], #620 llamadas sugeridas para los vendedores de grupo, #668 fecha permitida, #650 privadas, #678
    nuevos del mes en el motor de stock, #646 varias PDA, #595 slice 2 [interruptor apagado] y slice 5, fuera Correos Express,
    #670/#671 sugerencias + modelo [sin activar], #679 enlaces de pago, #681 referencia del proveedor) + Nesto (#537, #532,
    #536, #539, incidencia 687, aviso de empaquetado en Agencias, #679, #490 4C.5 tramos 3b y 3c sin cambio visible).

    ORDEN:
      1) Scripts sa ANTES de publicar la API (todos idempotentes; sin ellos la API funciona como hoy):
         - Issue668_FechaCompraPermitida_Grants.sql
         - Issue650_SugerenciasPrivadas.sql
         - Issue671a_SugerenciasProductosModelo.sql
         - Issue670b_SugerenciasDescartadoVendedor.sql
         - Nesto595_ParametroInsertarEnvioPorApi.sql           (nace apagado para todos)
         - Nesto537_ParametroSugerenciasPlantillaPlegadas.sql
         - Nesto595_ParametroFacturarAlImprimirEtiqueta.sql    (opcional; nace apagado)
         - OneShot_20261020_BorrarSugerencia688.sql
      2) Publicar NestoAPI.
      3) Publicar la ClickOnce (Clean + Rebuild) → 1.10.45.0.
      4) DESPUÉS: ESTE script. Adjuntar a la novedad «Puede ofrecer» la guía Documentacion\Guia sugerencias plantilla.pdf.
      5) Encolar el entrenamiento del modelo de sugerencias: POST api/SugerenciasProductos/ReentrenarModelo (→ hoy 23:30).

    SE OMITE A PROPÓSITO:
      - #595 «crear el envío en el servidor» (InsertarEnvioPorApi apagado; piloto Alfredo tras una semana de sombra).
      - Correos Express: ya no se usaba; los envíos antiguos se siguen viendo.
      - #658 informe de lo facturado en tienda: sin pantalla en Nesto todavía.
      - #660 correo «Gracias por su pedido» (lo ven los clientes de la app, no los usuarios de Nesto).
      - #671 modelo de sugerencias: se entrena esta noche y no se activa hasta ver el informe.
      - #490 4C.5 tramos 3b y 3c: sin cambio visible.
      - Lo de Ariadna (varias PDA, etiqueta de agencia, corregir una parada, referencia del proveedor…): en su script.

    Ejecutar en SSMS contra NV como sa. Los literales van con N'...' (columnas nvarchar).
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.45.0';
DECLARE @fecha DATE = '2026-10-20';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000), Perfiles nvarchar(200));

INSERT INTO @novedades (Categoria, Titulo, Descripcion, Perfiles) VALUES
    ('Nuevo', N'«Puede ofrecer»: ideas para vender más en cada pedido',
     N'En la plantilla de venta, debajo de la lista de productos, Nesto le propone tres productos que a ese cliente le pueden encajar: lo que ya le toca reponer, lo que va con lo que lleva, lo que compran clientes parecidos y lo que conviene potenciar. Cada tarjeta lleva foto, tamaño y familia. Con un clic le dice a Nesto si ha acertado: «✓» lo añade al pedido, «✗» si se lo ha ofrecido y no lo quiere, y «🚫 Quitar» si ni se lo va a ofrecer. Así las siguientes sugerencias son cada vez más a su gusto. No es obligatorio: si no toca nada, no pasa nada. En la guía adjunta tiene todos los detalles.',
     N'Vendedores,Tiendas'),
    ('Nuevo', N'Sugerencias privadas',
     N'Al enviar una sugerencia puede marcar «Privada»: solo la verán usted, Dirección e Informática. Le recomendamos dejarlas públicas siempre que pueda, porque así sus compañeros pueden apoyarlas y comentarlas.',
     NULL),
    ('Mejorado', N'Llamadas sugeridas: también para los vendedores de peluquería',
     N'En «Clientes para contactar», los vendedores que llevan clientes por grupo de producto (peluquería) ya tienen su lista, con sus propias compras para calcular cuándo toca llamar. Si un cliente ha hecho un pedido hace poco, de lo que sea, no se propone a ninguno de sus vendedores.',
     N'Vendedores'),
    ('Mejorado', N'Enviar reposición: los cambios de cantidad no se pierden',
     N'Si deja «Enviar reposición» abierta y cambia a otras pestañas, al volver la pantalla se pone al día sola y lo que estuviera escribiendo se guarda. Además, cada cantidad que baja se confirma abajo («Guardado: 42652 → 5»), para que sepa que se ha guardado.',
     N'Tiendas'),
    ('Mejorado', N'Conciliación: compras cerradas, primera fecha permitida',
     N'Al contabilizar desde la conciliación una factura de compra con fecha de un periodo ya cerrado, Nesto le pregunta si quiere contabilizarla con la primera fecha permitida. El pago se queda con la fecha del banco.',
     N'Administración'),
    ('Mejorado', N'Agencias: enlaces de pago y contra reembolso',
     N'Al insertar un envío contra reembolso, si el pedido tiene un enlace de pago sin pagar, Nesto le pregunta si de verdad sale contra reembolso. Lo que el cliente ya ha pagado con un enlace cuenta como prepago y se puede descontar del reembolso. Y si un cliente paga un enlace cuando su pedido ya va en reparto contra reembolso, Logística y Administración reciben un aviso para avisar a la agencia.',
     N'Almacén,Administración,Vendedores'),
    ('Mejorado', N'Agencias: aviso si alguien está empaquetando el pedido en Ariadna',
     N'Si al sacar la etiqueta alguien está empaquetando ese pedido en Ariadna en ese momento, Nesto se lo indica, para que no se quede ninguna caja fuera.',
     N'Almacén'),
    ('Mejorado', N'Ficha de control de stock: productos nuevos de este mes',
     N'Los productos que se empiezan a vender en el mes en curso ya no salen con máximo y mínimo sugeridos 0: se calculan con lo vendido hasta hoy, con prudencia al principio, y bajan si las ventas de las últimas semanas caen más de un 20 %.',
     N'Compras'),
    ('Corregido', N'Agencias: ampliar un envío ya registrado en CTT o Innovatrans',
     N'Ampliar un envío de CTT o Innovatrans que ya estaba registrado en la agencia borraba su número de seguimiento. Ahora no se permite: Nesto le avisa de que lo anule y lo vuelva a insertar.',
     N'Almacén'),
    ('Corregido', N'Incidencias de agencia: el correo va con copia a Logística',
     N'El correo «Incidencia de la agencia en el pedido…» que reciben los comerciales va ahora con copia a Logística, que es quien lo gestiona, en lugar de a Administración.',
     N'Almacén,Administración,Vendedores');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa', n.Perfiles
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesNestoInsertadasAhora;
