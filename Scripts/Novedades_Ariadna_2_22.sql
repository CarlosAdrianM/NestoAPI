/*
    Novedades de Ariadna 2.22 (29/10/26): Ariadna#60, #61 y #63 (gemelas de NestoAPI#734 y #739).
    Necesita la API publicada con 6831d7df o posterior y los scripts Issue739_RepartoPropio, Issue739_CargaFurgoneta y
    Ariadna63_VerHistoricoAlmacen. Ya está en el canal INTERNO (29/10, 17:01). Ejecutar en SSMS contra NV como sa cuando la
    2.22 llegue a la prueba cerrada. Idempotente (compara por Ámbito + Título).
    Avisos al publicar: 872/873 (@Pedro @Andre: filtro por tipo, histórico, carga de furgoneta) y a los repartidores.
*/
SET NOCOUNT ON;
USE NV;
DECLARE @n TABLE (Orden int, Categoria nvarchar(20), Titulo nvarchar(200), Descripcion nvarchar(max), Perfiles nvarchar(200));
INSERT INTO @n VALUES
(1, N'Nuevo', N'Reparto (9): entregar nuestra ruta desde Ariadna',
 N'En el inicio hay un botón nuevo, «Reparto» (tecla 9), con los envíos de reparto propio de hoy. Al leer el código de un bulto sale su ficha: puede llamar al cliente (F3), entregar (F1 o Intro) con el nombre de quien recoge, su DNI o su firma con el dedo y el cobro del reembolso, o dar una incidencia (F2) con su motivo. Funciona sin cobertura: lo hecho se guarda y sube solo al volver la red.',
 N'Almacén'),
(2, N'Nuevo', N'Cobro del reembolso: efectivo o enlace de pago',
 N'Al entregar, el reembolso se cobra en efectivo, con un enlace de pago que llega al correo que diga el cliente (F6), o se marca como no cobrado. El efectivo se apunta en su caja. Con «Mis cobros» (F4) ve el saldo anterior, los cobros del día y el saldo final de su caja, día a día con las flechas.',
 N'Almacén'),
(3, N'Nuevo', N'Cargar la furgoneta escaneando los bultos',
 N'En Reparto, el modo «Cargar» (F2) apunta cada bulto que lee como cargado por usted. Si ya lo lleva otro, puede quedárselo con «Me lo llevo yo». La lista enseña por defecto sus envíos (F3 para ver todos) y «Sin cargar» (F6) lo que queda en el almacén.',
 N'Almacén'),
(4, N'Nuevo', N'Histórico de pickings y packings',
 N'En Salidas, F6 abre el histórico (si tiene permiso): pickings y packings de un rango de fechas (ayer y hoy por defecto, hasta 31 días), por pedido o por producto, con quién recogió cada línea, qué faltó, y el contenido y la foto de cada caja.',
 N'Almacén'),
(5, N'Mejorado', N'Salidas y Entradas: filtro por tipo',
 N'En Salidas y en Entradas hay un desplegable de tipo (F3) para ver solo un tipo de movimiento. Ariadna recuerda el que eligió en cada pantalla.',
 N'Almacén'),
(6, N'Mejorado', N'Packing: pedido con enlace de pago pendiente',
 N'Si al pedir la etiqueta de agencia (F8) el pedido es contra reembolso y tiene un enlace de pago sin pagar, Ariadna ofrece retenerlo y consultar a Administración. La respuesta llega en Avisos (F10) y, mientras tanto, el pedido sale como «Retenido: esperando a Administración».',
 N'Almacén');
INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT '2.22', '2026-10-29', n.Categoria, n.Titulo, n.Descripcion, 'Ariadna', 1, 'sa', n.Perfiles
FROM @n n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = n.Titulo)
ORDER BY n.Orden;
SELECT @@ROWCOUNT AS NovedadesAriadnaInsertadasAhora;
