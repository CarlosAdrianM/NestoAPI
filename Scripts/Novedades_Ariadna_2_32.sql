/*
    Novedades de Ariadna 2.32 (09/11/26): parte Ariadna de NestoAPI#784 (inventario: «¿Contar otra vez?», F6 con el 0 confirmado y el
    motivo de los rechazos), de NestoAPI#590 (si se saca un pedido del picking desde Nesto, la PDA lo sabe al momento), de NestoAPI#844
    (el cierre de caja del repartidor deja de ser opcional) y «Cerrada» en las sugerencias. Necesita la API del 09/11 (master tras d8b41aee).
    Ejecutar en SSMS contra NV como sa cuando la 2.32 llegue a la prueba cerrada, y DESPUÉS Issue844_GlobosCierreCaja.sql (globos de
    «Cerrar caja», VersionDesde 2.32). Idempotente (compara por Ámbito + Título).
    NO duplicar: las sugerencias 1135 (Pedro, el motivo al terminar el picking) y 1133 (Paloma, aviso a la tienda por una diferencia de
    reposición) las marca Implementadas en 2.32 el OneShot (la sugerencia ES la novedad); aquí no llevan fila. Sus duplicadas 1143 y 1134
    las cierra el OneShot.
*/
SET NOCOUNT ON;
USE NV;
DECLARE @n TABLE (Orden int, Categoria nvarchar(20), Titulo nvarchar(200), Descripcion nvarchar(max), Perfiles nvarchar(200));
INSERT INTO @n VALUES
(1, N'Nuevo', N'Gracias, Pedro, Paloma y Alfredo',
 N'El picking que se puede terminar aunque la oficina haya sacado o albaranado pedidos, que la PDA diga siempre por qué no deja seguir y el inventario que ya no se vuelve a abrir sin querer salen de lo que nos han contado hoy por «Avisar». Muchas gracias: sigan contándonos.',
 NULL),
(2, N'Mejorado', N'Inventario: «¿Contar otra vez?» y el 0 confirmado con F6',
 N'Si lo que va a contar (la tienda entera o un producto) ya se contabilizó hoy, Ariadna lo dice y pregunta «¿Contar otra vez? Intro sí · Esc no»: con No vuelve atrás sin tocar nada. Ya no se crean filas repetidas de un producto: si ya tiene fila, se suma a esa. Al contabilizar con F6, si un producto se volvió a abrir y sigue con 0 contado, pregunta «¿Los ha contado y de verdad hay 0?». Cuando algo no deja seguir, se ve el motivo.',
 N'Almacén,Tiendas'),
(3, N'Nuevo', N'Picking: si la oficina saca un pedido del picking, la PDA lo sabe al momento',
 N'Si desde Nesto se quita el picking de un pedido que se está recogiendo, la recogida se actualiza sola: las paradas de ese pedido desaparecen (sin perder la parada en la que está) y sale el aviso fijo «PEDIDO SACADO DEL PICKING» con lo que hay que apartar para ese pedido («aparte 20 Agujas Miliun (12485) para ese pedido»). Si está a medias de algo, el aviso espera a que termine.',
 N'Almacén'),
(4, N'Mejorado', N'Reparto: cerrar la caja deja de ser opcional',
 N'Al pulsar «Terminar jornada» con dinero en la caja, Ariadna dice cuánto lleva y abre «Cerrar caja» (el importe hay que contarlo y teclearlo). Si al día siguiente queda dinero de días anteriores sin cerrar, no deja cargar ni recoger hasta cerrar la caja. Un cobro de deuda hecho en ese momento no se pierde: se queda guardado en el aparato y sube solo al cerrar la caja. Si lo contado no cuadra, se avisa a Administración. «Cerrar caja» está en el menú «⋯» y con F8 en «Mis cobros».',
 N'Almacén'),
(5, N'Mejorado', N'Sugerencias: las contestadas pasan a «Cerrada»',
 N'Una sugerencia ya contestada y sin nada más que hacer pasa a «Cerrada». Quien la escribió la sigue viendo 14 días, con «Cerrada: si necesita algo más, escriba y se reabre»; en cuanto alguien escribe en ella, vuelve a estar abierta.',
 NULL);

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT '2.32', '2026-11-09', n.Categoria, n.Titulo, n.Descripcion, 'Ariadna', 1, 'sa', n.Perfiles
FROM @n n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = n.Titulo)
ORDER BY n.Orden;
SELECT @@ROWCOUNT AS NovedadesAriadnaInsertadasAhora;
