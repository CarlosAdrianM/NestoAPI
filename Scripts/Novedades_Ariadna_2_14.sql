/*
    Novedades de Ariadna 2.14 (20/10/26): NestoAPI#646 (varias PDA), Ariadna#13 (etiqueta de agencia, #595 slice 5),
    Ariadna#41 (corregir una parada), Ariadna#48 (aviso fijo al fallar Terminar), Ariadna#49 (referencia del proveedor y código
    compartido), Ariadna#32 (sugerencias privadas) y Ariadna#30 (guarda de «usted»).
    Va primero al canal INTERNO (Carlos). Ejecutar en SSMS contra NV como sa cuando la 2.14 llegue a la prueba cerrada.
    Idempotente (compara por Ámbito + Título). Necesita la API del 20/10 publicada.
    Al publicar: comentario en la novedad «Corregir una parada» con @Andre @Pedro @Santi @Alfredo pidiendo que prueben
    azul + * en el recorrido (¿abre «Corregir la parada»? si no, ¿escribe «+»?), y contestar a Andre lo de la referencia del
    proveedor y los códigos compartidos.
*/
SET NOCOUNT ON;
USE NV;
DECLARE @n TABLE (Orden int, Categoria nvarchar(20), Titulo nvarchar(200), Descripcion nvarchar(max), Perfiles nvarchar(200));
INSERT INTO @n VALUES
(1, N'Nuevo', N'Etiqueta de agencia desde el packing',
 N'Al terminar una entrega en el packing, con todo dentro y las cajas con foto, pulse F8 «Etiqueta de agencia» y teclee el peso (la coma, con * o #). Si es de CTT o Innovatrans, se crea el envío, se registra en la agencia y la etiqueta sale por su impresora. Si es de GLS, el envío queda grabado y la etiqueta se saca en Agencias de Nesto. Pulsar F8 otra vez solo vuelve a imprimirla.',
 N'Almacén'),
(2, N'Nuevo', N'Varias PDA en el mismo picking o packing',
 N'Varias personas pueden preparar a la vez el mismo picking o empaquetar el mismo pedido. Cada PDA ve al momento lo que coge la otra; antes de dar algo por falta, Ariadna le avisa si otra persona ya lo ha leído; en el packing, si dos cierran la misma caja a la vez, ninguna foto se pisa; y en la lista se ve «Lo está empaquetando Pedro (hace 2 min)».',
 N'Almacén'),
(3, N'Nuevo', N'Corregir una parada concreta',
 N'En el recorrido puede corregir una parada aunque la haya leído otra PDA o haya cerrado la pantalla: elíjala (con F5 ve la lista completa) y pulse la tecla azul y después * (o toque «Corregir la parada»). Puede quitar la falta (F3) o lo cogido (cuántas + Intro), y confirma con Intro. F2 sigue deshaciendo lo último.',
 N'Almacén'),
(4, N'Nuevo', N'Referencia del proveedor a la vista',
 N'Bajo el nombre del producto sale «Ref. prov.: …», que ayuda a distinguir productos muy parecidos cuyo código de barras no trae los números impresos. Y si un código lo llevan varios productos de la lista, Ariadna le pregunta siempre cuál ha cogido: elija con ↑↓ e Intro, con 1/2/3 o tocando; Esc no cuenta nada.',
 N'Almacén,Tiendas'),
(5, N'Mejorado', N'Si «Terminar» falla, se entera seguro',
 N'Si al terminar una recepción, un recorrido, un envío o una caja el servidor no lo acepta, suena el aviso de error y aparece un recuadro rojo («NO SE HA RECIBIDO», «NO SE HA TERMINADO»…) con el motivo y qué hacer. No se va solo: se cierra con Intro, Esc o tocándolo, y puede seguir leyendo mientras tanto.',
 N'Almacén,Tiendas'),
(6, N'Nuevo', N'Sugerencias privadas',
 N'Al enviar una sugerencia puede marcarla como privada (F8): solo la verán usted, Dirección e Informática.',
 NULL);
INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT '2.14', '2026-10-20', n.Categoria, n.Titulo, n.Descripcion, 'Ariadna', 1, 'sa', n.Perfiles
FROM @n n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = n.Titulo)
ORDER BY n.Orden;
SELECT @@ROWCOUNT AS NovedadesAriadnaInsertadasAhora;
