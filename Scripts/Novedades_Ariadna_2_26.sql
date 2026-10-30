/*
    Novedades de Ariadna 2.26 (30/10/26): Ariadna#64, #65, #66, #67, #68, #69, #70 y la parte de Ariadna de NestoAPI#762 y #769.
    Necesita la API publicada con lo del 30/10 y los scripts Issue759_ParadasReparto, Issue767_DireccionesCoordenadas e
    Issue763_HorarioPreferible lanzados (los dos primeros ya están; el tercero se lanza al publicar la API).
    Ejecutar en SSMS contra NV como sa cuando la 2.26 llegue a la prueba cerrada. Idempotente (compara por Ámbito + Título).
    Al publicar: contestar a Miguel en la sugerencia 903 (paradas, recogidas, reposición, cobro libre, orden sugerido).
*/
SET NOCOUNT ON;
USE NV;
DECLARE @n TABLE (Orden int, Categoria nvarchar(20), Titulo nvarchar(200), Descripcion nvarchar(max), Perfiles nvarchar(200));
INSERT INTO @n VALUES
(1, N'Nuevo', N'Reparto: paradas de entrega, recogida y reposición',
 N'La lista de Reparto son ya paradas: los envíos de agencia, las notas de entrega (NE) y de recogida (NR), cada una con su color, y las reposiciones de las tiendas, que se añaden leyendo la etiqueta del traspaso. Al cargar la furgoneta (F2) la parada queda a su nombre y solo ve las suyas (F3 para ver todas); si otro lee un bulto de una parada suya, Ariadna se lo dice y puede quedársela. En una recogida se leen los bultos que se recogen o se marca que no había nada que recoger. El orden se cambia arrastrando y se guarda.',
 N'Almacén'),
(2, N'Nuevo', N'Reparto: «Ir» y «Ruta completa» en el mapa',
 N'En la ficha de la parada, «Ir» (F1) abre el navegador del móvil con esa dirección; en la lista, «Ruta completa» (F8) abre el mapa con todas las paradas pendientes en su orden.',
 N'Almacén'),
(3, N'Nuevo', N'Reparto: orden sugerido de las paradas',
 N'En el menú «⋯», «Orden sugerido» ordena las paradas pendientes desde donde está ahora hasta donde quiera acabar: Algete (1), su casa (2, se guarda solo en su aparato) o aquí mismo (3). Enseña los kilómetros y los minutos y la hora estimada de llegada a cada parada. Si algún cliente tiene horario preferible, intenta ordenarlas para llegar dentro de él y avisa de las que llegarían fuera: es una preferencia, no un compromiso. Después puede cambiar el orden a mano.',
 N'Almacén'),
(4, N'Nuevo', N'Reparto: horario preferible del cliente en cada parada',
 N'Las paradas de clientes con horario preferible llevan un reloj con las franjas de hoy; en la ficha se ve el horario entero y si hoy no es su día preferible.',
 N'Almacén'),
(5, N'Nuevo', N'Reparto: cobrar una deuda (F7)',
 N'Con «Cobrar una deuda» (F7) elige el cliente, ve lo que tiene pendiente, marca lo que le paga en efectivo, teclea el importe y cobra: el cobro se apunta en su caja como en la caja de Nesto. Bizum y tarjeta van por su cuenta (enlace de pago).',
 N'Almacén'),
(6, N'Nuevo', N'Escanear para Nesto (*)',
 N'En el inicio, «Escanear para Nesto» (tecla *) convierte el móvil en el lector de su Nesto: lo que lea con la cámara llega al campo que tenga el cursor en el Nesto abierto con su mismo usuario, con su Intro.',
 N'Almacén,Tiendas,Administración'),
(7, N'Nuevo', N'Recibir la reposición solo leyendo',
 N'La reposición llega con todo etiquetado (lo que no tenía código de barras lo imprime el que la prepara con TAB) y se recibe leyendo cada unidad. Lo que llegue sin etiqueta se apunta con «Sin etiqueta» (F1), unidad a unidad, y se avisa al origen y a Compras.',
 N'Tiendas,Almacén'),
(8, N'Mejorado', N'Packing: notas de entrega de solo texto',
 N'Las notas de entrega que solo llevan texto («Preparar: …») salen en el packing como cualquier pedido: bultos, etiqueta y, si llevan reembolso, el importe en la etiqueta.',
 N'Almacén'),
(9, N'Corregido', N'Reparto: el menú «⋯» tenía la F3 repetida',
 N'En Reparto, «Tipo» del menú no aplicaba y repetía la tecla F3 de «Ver todos». Ya no sale.',
 N'Almacén');
INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT '2.26', '2026-10-30', n.Categoria, n.Titulo, n.Descripcion, 'Ariadna', 1, 'sa', n.Perfiles
FROM @n n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = n.Titulo)
ORDER BY n.Orden;
SELECT @@ROWCOUNT AS NovedadesAriadnaInsertadasAhora;
