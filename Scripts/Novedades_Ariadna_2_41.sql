/*
    Ariadna 2.41 (20/11/26): la lista de reparto solo con lo que lleva cada repartidor, «No se ha recogido» en las recogidas
    y «Ver todos los envíos» solo en el menú. NECESITA la API del 20/11/26 publicada ANTES.
    Los envíos internos entre sedes (sugerencia 1277) y el comentario al recoger (1315) se contestan en su hilo.
    Ejecutar en SSMS contra NV como sa cuando la 2.41 llegue a las PDA. Idempotente.
*/
SET NOCOUNT ON;
USE NV;

DECLARE @n TABLE (Orden int, Categoria nvarchar(20), Titulo nvarchar(200), Descripcion nvarchar(max), Perfiles nvarchar(200));
INSERT INTO @n VALUES
(1, N'Nuevo', N'Reparto: en la lista solo salen sus paradas',
 N'La lista de Reparto enseña ya solo lo que usted lleva: las paradas de las que ha leído algún bulto (o el papel de la nota de entrega o de recogida que le han dado). Las de sus compañeros no salen. Si una parada no le aparece, es que no la ha cargado: lea su bulto o su papel y se le añade. Al terminar de cargar, Ariadna le avisa de lo que se queda en el almacén sin que lo lleve nadie.',
 NULL),
(2, N'Nuevo', N'Reparto: «No se ha recogido» en una recogida',
 N'En la ficha de una nota de recogida tiene «No se ha recogido» (F5) para cuando no hay nada que recoger o no hay nadie. Escriba el motivo: la recogida pasa a Almacén, que decide si vuelve a salir otro día o se cierra. Ya no se queda en su lista.',
 NULL),
(3, N'Mejorado', N'Reparto: «Ver todos los envíos», solo en el menú',
 N'«Ver todos los envíos» está ahora solo en el menú «⋯» y la tecla F3 de la lista ya no hace nada, para no cambiar de lista sin querer.',
 NULL),
(4, N'Mejorado', N'Reparto: si un envío lo lleva otro compañero, se lo dice',
 N'Cuando no se puede hacer algo con un bulto porque lo lleva otro repartidor o porque Almacén tiene que decidir, Ariadna enseña el motivo tal cual y ya no le pregunta si se lo queda usted.',
 NULL);

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT '2.41', '20261120', n.Categoria, n.Titulo, n.Descripcion, 'Ariadna', 1, 'sa', n.Perfiles
FROM @n n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = n.Titulo)
ORDER BY n.Orden;
SELECT @@ROWCOUNT AS NovedadesAriadnaInsertadasAhora;
