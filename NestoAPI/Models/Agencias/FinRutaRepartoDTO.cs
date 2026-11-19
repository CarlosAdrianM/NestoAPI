using System;
using System.Collections.Generic;

namespace NestoAPI.Models.Agencias
{
    // NestoAPI#881: el fin previsto de la ruta del reparto propio (cortes 1 a 3). JSON en PascalCase, como el resto de api/RepartoPropio.

    /// <summary>
    /// NestoAPI#881: el fin previsto de la ruta. Lo devuelve POST api/RepartoPropio/Estimacion (el orden vigente, sin reordenar ni
    /// guardar) y va dentro de la respuesta de POST api/RepartoPropio/OrdenSugerido (<see cref="OrdenSugeridoRepartoDTO.FinRuta"/>,
    /// con el orden nuevo). Nunca falla: si falta algo (coordenadas, el motor de rutas) se estima y sale <see cref="Aproximada"/>.
    /// </summary>
    public class EstimacionFinRutaDTO
    {
        public const string DESTINO_ALMACEN = "Almacen";
        public const string DESTINO_PERSONALIZADO = "Personalizado";

        /// <summary>
        /// La hora a la que se calcula que termina. Si la ruta pasa por Algete (acaba allí, o acaba en otro sitio pero lleva algo que
        /// dejar en Algete), la hora a la que termina de descargar en Algete; si no, la de llegada al destino.
        /// </summary>
        public DateTime FinEstimado { get; set; }
        /// <summary>
        /// Los minutos que quedan de ruta desde <see cref="CalculadoEl"/> hasta <see cref="FinEstimado"/> (conducción con colchón + paradas +
        /// descarga). La PDA pinta «ahora + MinutosRestantes» y lo repinta en local cada minuto: si no sale, el fin se va retrasando solo.
        /// </summary>
        public int MinutosRestantes { get; set; }
        /// <summary>
        /// Desde cuándo se cuentan <see cref="MinutosRestantes"/>: hoy, la hora de la PDA (FechaHora de la petición, o la del servidor); un día
        /// futuro, las 9:00 de ese día (= <see cref="HoraSalida"/>).
        /// </summary>
        public DateTime CalculadoEl { get; set; }
        /// <summary>El cierre como hora del día («17:00», «15:00»); null si ese día no hay cierre (como <see cref="HoraCierre"/>).</summary>
        public string HoraCierreDelDia { get; set; }
        /// <summary>La hora de llegada al destino elegido (casa u otro punto). Igual que <see cref="FinEstimado"/> si acaba en Algete.</summary>
        public DateTime LlegadaDestino { get; set; }
        /// <summary>Minutos de conducción de toda la ruta (vuelta incluida) CON el colchón (+15 % sobre el motor de rutas).</summary>
        public int MinutosConduccion { get; set; }
        /// <summary>Minutos en las paradas (los de cada tipo; las de la misma dirección cuentan una vez).</summary>
        public int MinutosParadas { get; set; }
        /// <summary>Minutos de descarga en Algete (15 si la ruta pasa por Algete; 0 si no).</summary>
        public int MinutosDescarga { get; set; }
        /// <summary>Kilómetros de toda la ruta (vuelta incluida), sin colchón.</summary>
        public decimal Km { get; set; }
        /// <summary>El cierre de Algete de ese día (L-J 17:00, V 15:00). Null en sábados, domingos y días sin reparto propio.</summary>
        public DateTime? HoraCierre { get; set; }
        /// <summary>Hay cierre ese día y la ruta pasa por Algete (si acaba en casa sin nada para Algete, no se compara).</summary>
        public bool SeComparaConCierre { get; set; }
        public bool PasaDelCierre { get; set; }
        /// <summary>Minutos después del cierre (0 si no pasa o no se compara).</summary>
        public int MinutosDeMas { get; set; }
        /// <summary>El texto listo para pintar, de usted: «Fin previsto 17:40 · 40 min después del cierre» o «Fin previsto 15:10».</summary>
        public string Aviso { get; set; }
        /// <summary>
        /// True si alguna parada va por el centro de su código postal o sin coordenadas, o si el motor de rutas no ha contestado y se
        /// ha estimado en línea recta (× 1,3 a 30 km/h).
        /// </summary>
        public bool Aproximada { get; set; }
        /// <summary>"OSRM" o "Heuristica" (línea recta).</summary>
        public string Motor { get; set; }
        /// <summary>"Almacen" o "Personalizado" (DestinoLatitud/DestinoLongitud de la petición).</summary>
        public string Destino { get; set; }
        /// <summary>True si la ruta pasa por Algete antes del destino (acaba en casa pero lleva recogidas o reposiciones de vuelta).</summary>
        public bool PasaPorAlgete { get; set; }
        /// <summary>Desde qué hora se calcula (la de la PDA; un día futuro, las 9:00).</summary>
        public DateTime HoraSalida { get; set; }
        /// <summary>Paradas pendientes que entran en el cálculo.</summary>
        public int NumeroParadas { get; set; }
        /// <summary>
        /// NestoAPI#881 (corte 3): «Regla» (las constantes) o «Modelo» (el modelo en sombra, solo con ParametrosUsuario «(defecto)»
        /// Reparto.TiempoParadaQuienManda = «Modelo» y modelo guardado): de dónde salen los minutos de las paradas.
        /// </summary>
        public string MinutosDe { get; set; } = "Regla";
        /// <summary>Cada parada pendiente, en el orden en que se recorren.</summary>
        public List<ParadaEstimadaDTO> Paradas { get; set; } = new List<ParadaEstimadaDTO>();
    }

    /// <summary>NestoAPI#881: una parada en <see cref="EstimacionFinRutaDTO.Paradas"/>.</summary>
    public class ParadaEstimadaDTO
    {
        /// <summary>El EnvioId de la parada (negativo en las de reposición), como en GET Envios.</summary>
        public int EnvioId { get; set; }
        /// <summary>1..n en el orden de recorrido.</summary>
        public int Orden { get; set; }
        /// <summary>A qué hora se calcula que se llega (en minutos enteros).</summary>
        public DateTime LlegadaEstimada { get; set; }
        /// <summary>Lo que se cuenta en esa parada (0 si está en la misma dirección que otra que ya cuenta).</summary>
        public int MinutosParada { get; set; }
        /// <summary>La parada va por el centro de su código postal o sin coordenadas.</summary>
        public bool Aproximada { get; set; }
    }

    /// <summary>NestoAPI#881 (corte 2): un tipo de servicio técnico (GET/POST api/RepartoPropio/TiposServicioTecnico).</summary>
    public class TipoServicioTecnicoDTO
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
        /// <summary>Los minutos que se calculan para ese servicio; null = los de por defecto (60, parámetro Reparto.MinutosServicioTecnico).</summary>
        public int? MinutosEstimados { get; set; }
        public bool Activo { get; set; }
    }

    /// <summary>NestoAPI#881: POST api/RepartoPropio/TiposServicioTecnico. Un nombre ya existente devuelve el que hay (sin duplicar).</summary>
    public class CrearTipoServicioTecnicoDTO
    {
        public string Nombre { get; set; }
        public int? MinutosEstimados { get; set; }
    }

    /// <summary>
    /// NestoAPI#881: el servicio técnico de una nota de entrega o de recogida (PUT api/NotasEntrega/{empresa}/{pedido}/ServicioTecnico,
    /// y los mismos campos al confirmar una recogida solicitada). Sin tipo (ni Id ni Nombre) = la nota no lleva servicio técnico (se quita).
    /// </summary>
    public class PeticionServicioTecnicoNotaDTO
    {
        public int? TipoServicioTecnicoId { get; set; }
        /// <summary>Si no viene el Id: el nombre; si no existe, se crea el tipo.</summary>
        public string TipoServicioTecnicoNombre { get; set; }
        /// <summary>Opcional, tecleado: manda sobre los minutos del tipo. 1..600.</summary>
        public int? MinutosServicio { get; set; }
    }

    /// <summary>NestoAPI#881: el servicio técnico de una nota (respuesta de GET/PUT api/NotasEntrega/{empresa}/{pedido}/ServicioTecnico).</summary>
    public class ServicioTecnicoNotaDTO
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        /// <summary>Null si la nota no lleva servicio técnico.</summary>
        public int? TipoServicioTecnicoId { get; set; }
        public string ServicioTecnico { get; set; }
        /// <summary>Los tecleados en la nota (null si no se teclearon).</summary>
        public int? MinutosServicio { get; set; }
        /// <summary>Los que se calculan: los tecleados, si no los del tipo, si no los de por defecto (60). Null sin servicio técnico.</summary>
        public int? MinutosEstimados { get; set; }
    }
}
