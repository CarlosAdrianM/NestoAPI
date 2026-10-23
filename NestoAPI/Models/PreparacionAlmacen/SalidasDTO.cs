using System.Collections.Generic;

namespace NestoAPI.Models.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#556: lo que contesta POST api/Almacen/Recogidas/{tipo}/{numero}/Terminar. La salida de mercancía es el
    /// espejo de la recepción: el mismo núcleo para cualquier tipo (PICK, REPO) y lo propio de cada uno en su estrategia.
    /// </summary>
    public class ResultadoTerminarSalidaDTO
    {
        public string Tipo { get; set; }
        public int Numero { get; set; }
        /// <summary>No queda nada por resolver: todo cogido o dado por falta.</summary>
        public bool Terminada { get; set; }
        /// <summary>Terminada y sin ninguna falta ni producto de más.</summary>
        public bool Completa { get; set; }
        public int UnidadesEnFalta { get; set; }
        /// <summary>Qué pasa ahora, para enseñárselo al mozo tal cual.</summary>
        public string Mensaje { get; set; }
        public List<DiferenciaPreparacionDTO> Productos { get; set; } = new List<DiferenciaPreparacionDTO>();
        /// <summary>Lo que se ha cambiado al terminar (o se cambiaría, en un ensayo), una frase por cambio.</summary>
        public List<string> Cambios { get; set; } = new List<string>();

        /// <summary>
        /// Ensayo (?ensayo=true, solo Admin o Dirección): el mismo código, dentro de una transacción que se deshace
        /// siempre. No se ha guardado nada.
        /// </summary>
        public bool Ensayo { get; set; }
        /// <summary>Las filas implicadas antes de terminar (solo en un ensayo).</summary>
        public List<FilaEnsayoDTO> FilasAntes { get; set; }
        /// <summary>Las mismas filas justo antes de deshacer (solo en un ensayo que ha ido bien).</summary>
        public List<FilaEnsayoDTO> FilasDespues { get; set; }
        /// <summary>El error real (procedimiento, trigger o restricción) si el ensayo ha fallado.</summary>
        public string ErrorEnsayo { get; set; }
    }

    /// <summary>Una fila de una tabla en un ensayo, con las columnas que importan en texto.</summary>
    public class FilaEnsayoDTO
    {
        public string Tabla { get; set; }
        public string Clave { get; set; }
        public string Datos { get; set; }
    }

    /// <summary>
    /// NestoAPI#646 / Ariadna#41: POST api/Almacen/Escaneos/Corregir. Quitar una falta o lo cogido de una parada concreta
    /// (la haya hecho la PDA que la haya hecho), solo si la parada sigue como la vio la app (<see cref="EsperadoCogidas"/> y
    /// <see cref="EsperadoFaltas"/>): así dos PDA que corrigen a la vez no dejan la falta en negativo.
    /// </summary>
    public class CorregirEscaneoDTO
    {
        /// <summary>El del escaneo en negativo que se guarda: un reenvío con el mismo no corrige dos veces.</summary>
        public System.Guid IdCliente { get; set; }
        /// <summary>PICK o REPO.</summary>
        public string TipoOrigen { get; set; }
        public int NumeroOrigen { get; set; }
        public string Producto { get; set; }
        /// <summary>El hueco de la parada (001/007/002 o 001007002): va con la falta quitada, como un «Deshacer».</summary>
        public string Hueco { get; set; }
        /// <summary>LineaRecogidaDTO.Orden de la parada.</summary>
        public int Orden { get; set; }
        /// <summary>FALTA (quitar lo dado por falta) o LECTURA (quitar lo cogido).</summary>
        public string Tipo { get; set; }
        /// <summary>Cuántas se quitan (en positivo).</summary>
        public int Unidades { get; set; }
        /// <summary>Lo cogido de la parada (Resuelto − Faltas) que vio la app.</summary>
        public int EsperadoCogidas { get; set; }
        /// <summary>Lo dado por falta de la parada que vio la app.</summary>
        public int EsperadoFaltas { get; set; }
        /// <summary>En una LECTURA, el método de la lectura que se quita (SCAN, ETIQ, TECLA). Sin él, TECLA.</summary>
        public string Metodo { get; set; }
        public string Dispositivo { get; set; }
        /// <summary>La hora del móvil. Sin ella, la del servidor.</summary>
        public System.DateTime? FechaEscaneo { get; set; }
    }

    /// <summary>NestoAPI#646: lo que contesta POST api/Almacen/Escaneos/Corregir (200, o 409 con cómo está ahora la parada).</summary>
    public class ResultadoCorregirEscaneoDTO
    {
        /// <summary>Se ha guardado (o ya estaba, si <see cref="Repetida"/>).</summary>
        public bool Corregida { get; set; }
        /// <summary>Ese IdCliente ya estaba guardado: no se ha vuelto a quitar nada.</summary>
        public bool Repetida { get; set; }
        /// <summary>La salida ya está terminada: no se ha tocado nada.</summary>
        public bool YaTerminada { get; set; }
        public string Mensaje { get; set; }
        /// <summary>La parada como está ahora en el servidor (después de corregir, o la que ha hecho que no se corrija). Null si ya no está.</summary>
        public LineaRecogidaDTO Linea { get; set; }
        /// <summary>Lo cogido de la parada ahora (Resuelto − Faltas).</summary>
        public int Cogidas { get; set; }
        public int Faltas { get; set; }
    }

    /// <summary>Ariadna#6: de qué mozo son las lecturas que se anulan.</summary>
    public class AnularLecturasDTO
    {
        public string Usuario { get; set; }
    }

    public class ResultadoAnularLecturasDTO
    {
        /// <summary>Las lecturas en negativo añadidas (0 si ese mozo no tenía nada subido).</summary>
        public int Filas { get; set; }
        public string Mensaje { get; set; }
    }

    /// <summary>Ariadna#12: otro hueco donde hay libre del producto que no estaba en el de la parada.</summary>
    public class HuecoAlternativoDTO
    {
        /// <summary>Como se enseña: 003/001/002.</summary>
        public string Ubicacion { get; set; }
        /// <summary>Como la etiqueta: 003001002.</summary>
        public string Codigo { get; set; }
        /// <summary>Unidades libres (sin reservar) en ese hueco.</summary>
        public int Cantidad { get; set; }
    }

    /// <summary>
    /// POST api/Almacen/Picking/{picking}/CambiarHueco y api/Almacen/Recogidas/{tipo}/{numero}/CambiarHueco (PICK y REPO):
    /// lo que no estaba en un hueco se coge de otro.
    /// </summary>
    public class CambiarHuecoPickingDTO
    {
        public string Producto { get; set; }
        /// <summary>El hueco de la parada, donde no estaba (001/007/002 o 001007002).</summary>
        public string HuecoOrigen { get; set; }
        /// <summary>El hueco de donde se va a coger.</summary>
        public string HuecoDestino { get; set; }
        public int Cantidad { get; set; }
    }

    public class ResultadoCambiarHuecoDTO
    {
        /// <summary>Unidades cuya reserva ha pasado al otro hueco (en un picking puede ser menos de lo pedido; en una reposición, todo o 409).</summary>
        public int Movidas { get; set; }
        public string Mensaje { get; set; }

        // Ariadna#12: con ?ensayo=true (Admin o Dirección) se hace todo en una transacción que se deshace siempre

        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public bool? Ensayo { get; set; }
        /// <summary>Ensayo: las filas de Ubicaciones del producto en los dos huecos (y lo nuevo pendiente de ubicar), antes.</summary>
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public List<FilaEnsayoDTO> FilasAntes { get; set; }
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public List<FilaEnsayoDTO> FilasDespues { get; set; }
        /// <summary>Ensayo: el error real si ha fallado (no se ha guardado nada).</summary>
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public string ErrorEnsayo { get; set; }
    }
}
