using System.Collections.Generic;

namespace NestoAPI.Models.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#642 (corte 4 de #639, para la ventana de Nesto#528): las calles del recorrido de un almacén tal como están
    /// guardadas (RecorridoCalles + RecorridoPasillos + Almacenes.FilaAMano), los pasillos con huecos que no van en ninguna calle
    /// y la vista previa de los tramos en el orden que resulta (la regla única <c>OrdenRecorrido</c>).
    /// GET api/Almacen/Recorrido y la respuesta de PUT api/Almacen/Recorrido.
    /// </summary>
    public class RecorridoAlmacenDTO
    {
        public string Empresa { get; set; }
        public string Almacen { get; set; }
        /// <summary>False si aún no se ha lanzado Scripts/Issue641_RecorridoCalles.sql: todo vacío y el orden, el de siempre.</summary>
        public bool TablasCreadas { get; set; }
        /// <summary>Desde qué fila se llega con la mano (las de arriba, 1 hasta FilaAMano - 1, son de escalera).</summary>
        public int FilaAMano { get; set; }
        /// <summary>En el orden del recorrido.</summary>
        public List<CalleRecorridoDTO> Calles { get; set; } = new List<CalleRecorridoDTO>();
        /// <summary>Pasillos con huecos en uso (o con nombre) que no van en ninguna calle: van al final, como siempre. Por pasillo.</summary>
        public List<PasilloRecorridoAlmacenDTO> PasillosSinCalle { get; set; } = new List<PasilloRecorridoAlmacenDTO>();
        /// <summary>Los tramos (pasillo + columna + nivel) de los huecos en uso, en el orden del recorrido.</summary>
        public List<TramoRecorridoDTO> Tramos { get; set; } = new List<TramoRecorridoDTO>();
        /// <summary>True en la respuesta de un PUT con ensayo=true: es lo que quedaría, no se ha guardado nada.</summary>
        public bool Ensayo { get; set; }
    }

    /// <summary>NestoAPI#642: una calle (pasillos que se recorren a la vez).</summary>
    public class CalleRecorridoDTO
    {
        /// <summary>RecorridoCalles.Id; null en un ensayo. Se ignora en el PUT (cada guardado vuelve a crear las calles).</summary>
        public int? Id { get; set; }
        /// <summary>Nombre corto («a», «b»…), hasta 10 caracteres, sin repetir en el almacén.</summary>
        public string Calle { get; set; }
        /// <summary>10, 20, 30…: en el PUT lo pone la API según el orden de la lista (se ignora lo que venga).</summary>
        public int Orden { get; set; }
        /// <summary>«A» columnas ascendentes (ida) o «D» descendentes (vuelta). En el PUT, vacío = «A».</summary>
        public string Sentido { get; set; }
        /// <summary>Hasta 100 caracteres. Opcional.</summary>
        public string Descripcion { get; set; }
        /// <summary>En el orden en que se recorren dentro de la calle (en el PUT, Posicion = sitio en la lista).</summary>
        public List<PasilloRecorridoAlmacenDTO> Pasillos { get; set; } = new List<PasilloRecorridoAlmacenDTO>();
    }

    /// <summary>NestoAPI#642: un pasillo del mantenimiento.</summary>
    public class PasilloRecorridoAlmacenDTO
    {
        /// <summary>Tres caracteres como en Ubicaciones («004»; en el PUT vale «4»).</summary>
        public string Pasillo { get; set; }
        /// <summary>1, 2…: el orden dentro de su calle. Null sin calle. Se ignora en el PUT.</summary>
        public int? Posicion { get; set; }
        /// <summary>Para que alguien nuevo lo encuentre («Esmaltes»), hasta 50 caracteres. Opcional.</summary>
        public string Nombre { get; set; }
        /// <summary>Huecos con algo ubicado en el pasillo (solo en la respuesta).</summary>
        public int Huecos { get; set; }
    }

    /// <summary>NestoAPI#642: un tramo de la vista previa: los huecos seguidos de un pasillo y columna en el mismo nivel.</summary>
    public class TramoRecorridoDTO
    {
        /// <summary>1, 2, 3…</summary>
        public int Orden { get; set; }
        /// <summary>El nombre de la calle; null si el pasillo no va en ninguna (va al final).</summary>
        public string Calle { get; set; }
        public string Pasillo { get; set; }
        public string Columna { get; set; }
        /// <summary>«AMano» o «Escalera» en un pasillo con calle; null sin calle (no hay niveles).</summary>
        public string Nivel { get; set; }
        /// <summary>Las filas del tramo, en el orden en que se cogen.</summary>
        public List<string> Filas { get; set; } = new List<string>();
    }

    /// <summary>
    /// NestoAPI#642: cuerpo de PUT api/Almacen/Recorrido. Sustituye TODO el recorrido del almacén: las calles que no vengan se borran
    /// y los pasillos que no vengan quedan sin calle (al final). El orden de <see cref="Calles"/> es el del recorrido.
    /// </summary>
    public class PeticionRecorridoAlmacenDTO
    {
        /// <summary>Null: no se toca. De 1 a 255.</summary>
        public int? FilaAMano { get; set; }
        public List<CalleRecorridoDTO> Calles { get; set; } = new List<CalleRecorridoDTO>();
        /// <summary>Solo para guardar el nombre de un pasillo que no va en ninguna calle (sin nombre, se ignora).</summary>
        public List<PasilloRecorridoAlmacenDTO> PasillosSinCalle { get; set; } = new List<PasilloRecorridoAlmacenDTO>();
    }
}
