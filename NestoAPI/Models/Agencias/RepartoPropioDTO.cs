using System;

namespace NestoAPI.Models.Agencias
{
    // NestoAPI#739: el contrato de api/RepartoPropio (comentario de la issue del 29/10/26). JSON en PascalCase.

    /// <summary>Un envío del reparto propio, como lo ve el repartidor en Ariadna.</summary>
    public class EnvioRepartoDTO
    {
        public const string ESTADO_TRAMITADO = "Tramitado";
        public const string ESTADO_INCIDENTADO = "Incidentado";
        public const string ESTADO_ENTREGADO = "Entregado";

        public int EnvioId { get; set; }
        public string Empresa { get; set; }
        public int? Pedido { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public string Direccion { get; set; }
        public string CodigoPostal { get; set; }
        public string Poblacion { get; set; }
        public string Telefono { get; set; }
        public string Movil { get; set; }
        /// <summary>
        /// El correo del cliente del envío (EnviosAgencia.Email, el del contacto del pedido): Ariadna lo propone para el cobro por
        /// enlace. Null si no hay o no es válido.
        /// </summary>
        public string Correo { get; set; }
        public int Bultos { get; set; }
        /// <summary>0 sin reembolso.</summary>
        public decimal Reembolso { get; set; }
        /// <summary>"Tramitado", "Incidentado" o "Entregado".</summary>
        public string Estado { get; set; }
        public string Observaciones { get; set; }
        /// <summary>
        /// Ariadna#63: quién lo lleva (el último que cargó un bulto suyo en la furgoneta), sin dominio. Null si nadie ha cargado
        /// nada (o sin Scripts/Issue739_CargaFurgoneta.sql).
        /// </summary>
        public string Repartidor { get; set; }
        /// <summary>Ariadna#63: bultos distintos que ha cargado el repartidor (0 sin repartidor).</summary>
        public int BultosCargados { get; set; }
        /// <summary>
        /// NestoAPI#759: "Envio", "NotaEntrega", "NotaRecogida" o "Reposicion" (<see cref="TiposParadaReparto"/>). Las de
        /// reposición llevan <see cref="EnvioId"/> NEGATIVO (−Id de RepartoParadasReposicion): se ordenan, se entregan y se buscan
        /// con ese mismo EnvioId, como las demás.
        /// </summary>
        public string TipoParada { get; set; } = TiposParadaReparto.ENVIO;
        /// <summary>
        /// NestoAPI#759: las líneas de texto de la nota separadas por \n (en las de reposición, una línea por traspaso con sus
        /// bultos cargados). Null en los envíos normales.
        /// </summary>
        public string TextoNota { get; set; }
        /// <summary>NestoAPI#759: el número de la nota (NotasEntrega.Numero), o null.</summary>
        public int? NumeroNota { get; set; }
        /// <summary>NestoAPI#759: «NE929667» / «NR929554» en las notas; null en las demás.</summary>
        public string CodigoDocumento { get; set; }
        /// <summary>
        /// NestoAPI#759: la posición que le dio su repartidor (PUT Orden), o null si nadie la ha ordenado. La lista ya viene por
        /// Orden (nulos al final), fecha y número.
        /// </summary>
        public int? Orden { get; set; }
        /// <summary>
        /// NestoAPI#767: dónde está la parada (WGS84), para «Ir», «Ruta completa» y el orden sugerido. En los envíos y las notas,
        /// la dirección de entrega geocodificada con Nominatim (de la caché DireccionesCoordenadas: nunca se busca en la petición;
        /// si aún no está, null y la geocodifica un job en segundo plano). En las de reposición, la tienda (coordenadas fijas).
        /// </summary>
        public decimal? Latitud { get; set; }
        /// <summary>NestoAPI#767: ver <see cref="Latitud"/>.</summary>
        public decimal? Longitud { get; set; }
        /// <summary>
        /// NestoAPI#763: el «horario preferible» del cliente/contacto de la parada, entero («L-V 10:00-14:00, 17:00-20:00; S
        /// 10:00-13:00»). Null si no tiene (o en las de reposición). Solo lo respeta nuestra ruta.
        /// </summary>
        public string HorarioPreferible { get; set; }
        /// <summary>
        /// NestoAPI#763: las franjas del horario preferible del DÍA de la lista («10:00-14:00, 17:00-20:00»). Null si no tiene
        /// horario; vacío ("") si tiene horario pero ese día no tiene ninguna franja (ese día no le viene bien).
        /// </summary>
        public string HorarioPreferibleDia { get; set; }
        /// <summary>
        /// NestoAPI#763: solo en la respuesta de POST OrdenSugerido. A qué hora se calcula que se llega (conducción + 5 minutos por
        /// parada, desde la hora de la PDA). Null si no se puede estimar (sin coordenadas, ya visitada).
        /// </summary>
        public DateTime? LlegadaEstimada { get; set; }
        /// <summary>NestoAPI#763: solo en POST OrdenSugerido. True si tiene horario preferible y la llegada estimada cae fuera de él.</summary>
        public bool FueraDeHorario { get; set; }

        /// <summary>Para ordenar la lista (no sale en el JSON): la fecha del envío o de la parada de reposición.</summary>
        internal DateTime FechaParada { get; set; }
    }

    /// <summary>NestoAPI#759: los tipos de parada del reparto propio.</summary>
    public static class TiposParadaReparto
    {
        public const string ENVIO = "Envio";
        public const string NOTA_ENTREGA = "NotaEntrega";
        public const string NOTA_RECOGIDA = "NotaRecogida";
        public const string REPOSICION = "Reposicion";
    }

    /// <summary>
    /// Ariadna#63: POST api/RepartoPropio/Cargar. El repartidor escanea al cargar cada bulto que se lleva. Idempotente por
    /// ClaveIdempotencia (la cola de Ariadna reintenta) y por bulto (el mismo bulto otra vez no da error).
    /// </summary>
    public class CargaBultoRepartoDTO
    {
        /// <summary>
        /// «RP{EnvioId}-{N}» (la etiqueta del bulto) y, NestoAPI#759, «NE{Pedido}», «NR{Pedido}», «NR{Pedido}-{N}» (notas) y
        /// «R{Traspaso}-{N}» (bultos de reposición: van a la parada «Reposición {tienda}» del día).
        /// </summary>
        public string Codigo { get; set; }
        public Guid ClaveIdempotencia { get; set; }
        /// <summary>La hora de la PDA. Null: la del servidor.</summary>
        public DateTime? FechaHora { get; set; }
        /// <summary>
        /// La parada es de otro repartidor (409 ENVIO_DE_OTRO_REPARTIDOR, «Este bulto no es suyo: es de la parada de X») y este
        /// confirma que se la lleva él (queda la traza en EnviosHistoria).
        /// </summary>
        public bool Reasignar { get; set; }
    }

    /// <summary>Ariadna#63: la respuesta de POST api/RepartoPropio/Cargar.</summary>
    public class ResultadoCargaBultoDTO
    {
        public int EnvioId { get; set; }
        public int Bulto { get; set; }
        public int Bultos { get; set; }
        /// <summary>Bultos distintos del envío cargados por este usuario.</summary>
        public int BultosCargados { get; set; }
        /// <summary>Quién lo lleva (este usuario), sin dominio.</summary>
        public string Repartidor { get; set; }
        /// <summary>El destinatario del envío.</summary>
        public string Nombre { get; set; }
        public string Poblacion { get; set; }
        /// <summary>NestoAPI#759: el tipo de la parada cargada (<see cref="TiposParadaReparto"/>).</summary>
        public string TipoParada { get; set; } = TiposParadaReparto.ENVIO;
        /// <summary>NestoAPI#759: «NE…»/«NR…» en las notas; null en las demás.</summary>
        public string CodigoDocumento { get; set; }
    }

    /// <summary>Ariadna#63: GET api/RepartoPropio/SinCargar, un envío tramitado de ese día con algún bulto sin cargar.</summary>
    public class EnvioSinCargarDTO
    {
        public int EnvioId { get; set; }
        public int? Pedido { get; set; }
        public string Nombre { get; set; }
        public string Poblacion { get; set; }
        public int Bultos { get; set; }
        public int BultosCargados { get; set; }
        /// <summary>Null si no lo ha cargado nadie.</summary>
        public string Repartidor { get; set; }
        /// <summary>NestoAPI#759: el tipo de parada (<see cref="TiposParadaReparto"/>).</summary>
        public string TipoParada { get; set; } = TiposParadaReparto.ENVIO;
        /// <summary>NestoAPI#759: «NE…»/«NR…» en las notas; null en las demás.</summary>
        public string CodigoDocumento { get; set; }
    }

    /// <summary>GET api/RepartoPropio/Bulto/{codigo}: el envío y el bulto escaneado.</summary>
    public class EnvioRepartoBultoDTO : EnvioRepartoDTO
    {
        public int Bulto { get; set; }
    }

    /// <summary>
    /// POST api/RepartoPropio/Envios/{envioId}/Entregar. Carlos 29/10/26: de momento solo es obligatoria la clave (registrar la
    /// entrega es opcional y no debe frenar el reparto); más adelante se exigirán receptor y firma o DNI.
    /// </summary>
    public class EntregaRepartoDTO
    {
        public Guid ClaveIdempotencia { get; set; }
        /// <summary>Hora local de la entrega (la de la PDA, aunque llegue más tarde por falta de cobertura). Sin ella, ahora.</summary>
        public DateTime? FechaHora { get; set; }
        public string Receptor { get; set; }
        public string DniReceptor { get; set; }
        /// <summary>PNG en base64.</summary>
        public string FirmaPng { get; set; }
        /// <summary>
        /// "Efectivo", "Enlace" o "NoCobrado". Con reembolso y sin esto, «NoCobrado». El repartidor no lleva datáfono: "Tarjeta"
        /// da 400. Efectivo se contabiliza al entregar en la caja del repartidor (su CajaDefecto); Enlace manda un enlace de pago
        /// a <see cref="CorreoEnlace"/> y no contabiliza nada (el cobro entra por la notificación de Redsys).
        /// </summary>
        public string CobroReembolso { get; set; }
        /// <summary>Lo cobrado (o el importe del enlace); sin él, el reembolso del envío.</summary>
        public decimal? ImporteCobrado { get; set; }
        /// <summary>Obligatorio y con formato válido con CobroReembolso = "Enlace": a dónde se manda el enlace de pago.</summary>
        public string CorreoEnlace { get; set; }
        public decimal? Latitud { get; set; }
        public decimal? Longitud { get; set; }
        public string Observaciones { get; set; }
        /// <summary>
        /// NestoAPI#759: solo en las notas de RECOGIDA, los bultos que se trae (0..999). Va a EnviosAgenciaPruebasEntrega.BultosRecogidos
        /// y a la historia («Reparto propio: recogido (2 bultos)»); en las demás paradas se ignora.
        /// </summary>
        public int? BultosRecogidos { get; set; }
        /// <summary>
        /// NestoAPI#772: los bultos de la parada que se han leído al entregar (opcional mientras RepartoEscaneoEntregaObligatorio
        /// esté apagado). Vacío o null: se dan por entregados todos los de la parada que llevaba encima. En las recogidas se ignora.
        /// </summary>
        public System.Collections.Generic.List<string> BultosEntregados { get; set; }
    }

    /// <summary>NestoAPI#759: PUT api/RepartoPropio/Orden. El orden de las paradas del repartidor, tal como lo deja en la PDA.</summary>
    public class OrdenRepartoDTO
    {
        /// <summary>La hora local de la PDA al ordenar (último gana: la cola puede llegar tarde). Sin ella, la del servidor.</summary>
        public DateTime? FechaHora { get; set; }
        /// <summary>Los EnvioId de las paradas en el orden nuevo (posición 1..n); las de reposición, negativos.</summary>
        public System.Collections.Generic.List<int> Envios { get; set; }
    }

    /// <summary>
    /// NestoAPI#767: POST api/RepartoPropio/OrdenSugerido. Todo opcional. Latitud/Longitud: la posición ACTUAL de la PDA (el
    /// recorrido sale de ahí: puede recalcular a media ruta); sin ellas (sin GPS o sin permiso), sale del almacén de Algete.
    /// </summary>
    public class PeticionOrdenSugeridoDTO
    {
        /// <summary>La hora local de la PDA (último gana, como PUT Orden). Sin ella, la del servidor.</summary>
        public DateTime? FechaHora { get; set; }
        /// <summary>El día de la lista (como GET Envios?fecha=). Sin él, hoy.</summary>
        public DateTime? Fecha { get; set; }
        public decimal? Latitud { get; set; }
        public decimal? Longitud { get; set; }
        /// <summary>
        /// Dónde acaba el recorrido si no es Algete (p. ej. la casa del repartidor): los dos o ninguno. La API no lo guarda (es un
        /// dato personal): lo guarda Ariadna en el aparato y lo manda en cada petición. Sin ellos, Algete.
        /// </summary>
        public decimal? DestinoLatitud { get; set; }
        public decimal? DestinoLongitud { get; set; }
    }

    /// <summary>NestoAPI#767: la respuesta de POST api/RepartoPropio/OrdenSugerido.</summary>
    public class OrdenSugeridoRepartoDTO
    {
        public const string MOTOR_OSRM = "OSRM";
        public const string MOTOR_HEURISTICA = "Heuristica";
        public const string MOTOR_SIN_COORDENADAS = "SinCoordenadas";
        public const string ORIGEN_POSICION = "Posicion";
        public const string ORIGEN_ALMACEN = "Almacen";
        public const string DESTINO_ALMACEN = "Almacen";
        public const string DESTINO_PERSONALIZADO = "Personalizado";

        /// <summary>
        /// Las paradas del repartidor en el orden nuevo (Orden = 1..n): primero las pendientes con coordenadas por el recorrido,
        /// luego las pendientes sin coordenadas (en su orden de antes) y al final las incidentadas (ya visitadas).
        /// </summary>
        public System.Collections.Generic.List<EnvioRepartoDTO> Paradas { get; set; } = new System.Collections.Generic.List<EnvioRepartoDTO>();
        /// <summary>"OSRM" (por carretera), "Heuristica" (OSRM no respondió: vecino más próximo + 2-opt en línea recta) o "SinCoordenadas".</summary>
        public string Motor { get; set; }
        /// <summary>"Posicion" (la de la PDA) o "Almacen" (Algete, porque no vino posición).</summary>
        public string Origen { get; set; }
        public decimal OrigenLatitud { get; set; }
        public decimal OrigenLongitud { get; set; }
        /// <summary>"Almacen" (Algete, por defecto) o "Personalizado" (el DestinoLatitud/DestinoLongitud de la petición).</summary>
        public string Destino { get; set; }
        public decimal DestinoLatitud { get; set; }
        public decimal DestinoLongitud { get; set; }
        /// <summary>Del origen a la última parada y de ahí al destino (Algete o el elegido). Con la heurística, estimados (línea recta × 1,3).</summary>
        public decimal? Kilometros { get; set; }
        /// <summary>Minutos de conducción (solo con OSRM; null con la heurística).</summary>
        public int? Minutos { get; set; }
        /// <summary>Paradas pendientes que aún no tienen coordenadas (van detrás de las que sí, sin mover).</summary>
        public int SinCoordenadas { get; set; }
        /// <summary>NestoAPI#763: paradas pendientes que, con este orden, llegarían fuera de su horario preferible.</summary>
        public int FueraDeHorario { get; set; }
        /// <summary>NestoAPI#763: el aviso para el repartidor si <see cref="FueraDeHorario"/> &gt; 0; null si no hay nada que avisar.</summary>
        public string AvisoHorario { get; set; }
        /// <summary>
        /// NestoAPI#769: true si se ha cambiado el recorrido más corto para intentar que más paradas lleguen dentro de su horario
        /// preferible (sin compromiso: el horario es solo una preferencia).
        /// </summary>
        public bool ReordenadoPorHorario { get; set; }
        /// <summary>NestoAPI#769: cuántos minutos más que el recorrido más corto tarda el orden final (conducción, paradas y esperas); 0 si no se reordena.</summary>
        public int MinutosExtra { get; set; }
        public int Guardados { get; set; }
        public int Ignorados { get; set; }
    }

    /// <summary>NestoAPI#759: la respuesta de PUT api/RepartoPropio/Orden.</summary>
    public class ResultadoOrdenRepartoDTO
    {
        public int Guardados { get; set; }
        /// <summary>Repetidos, que ya no están en reparto, de otro repartidor o con un orden más nuevo ya guardado.</summary>
        public int Ignorados { get; set; }
    }

    /// <summary>
    /// NestoAPI#759: el evento «repartoCambiado» del AlmacenHub, al grupo «Reparto». Sin datos de las paradas: quien lo recibe
    /// relee la lista (GET Envios).
    /// </summary>
    public class RepartoCambiadoDTO
    {
        public const string MOTIVO_ORDEN = "Orden";
        public const string MOTIVO_CARGA = "Carga";
        public const string MOTIVO_ENTREGA = "Entrega";
        public const string MOTIVO_INCIDENCIA = "Incidencia";
        /// <summary>NestoAPI#772: recoger, descargar, entregar bultos o terminar la jornada (la furgoneta ha cambiado).</summary>
        public const string MOTIVO_FURGONETA = "Furgoneta";

        /// <summary>Quién lo ha cambiado, sin dominio.</summary>
        public string Usuario { get; set; }
        /// <summary>"Orden", "Carga", "Entrega" o "Incidencia".</summary>
        public string Motivo { get; set; }
        public DateTime FechaHora { get; set; }
        /// <summary>El dispositivo que lo ha hecho (cabecera X-Dispositivo), o vacío.</summary>
        public string Dispositivo { get; set; }
    }

    /// <summary>POST api/RepartoPropio/Envios/{envioId}/Incidencia.</summary>
    public class IncidenciaRepartoDTO
    {
        public const string AUSENTE = "Ausente";
        public const string DIRECCION_INCORRECTA = "DireccionIncorrecta";
        public const string RECHAZADO = "Rechazado";
        public const string FALTAN_BULTOS = "FaltanBultos";
        public const string OTRO = "Otro";
        /// <summary>NestoAPI#759: solo en las notas de recogida.</summary>
        public const string NADA_QUE_RECOGER = "NadaQueRecoger";

        public Guid ClaveIdempotencia { get; set; }
        public DateTime? FechaHora { get; set; }
        /// <summary>
        /// "Ausente", "DireccionIncorrecta", "Rechazado", "FaltanBultos", "NadaQueRecoger" (solo recogidas, NestoAPI#759) u "Otro"
        /// (este exige <see cref="Texto"/>).
        /// </summary>
        public string Motivo { get; set; }
        public string Texto { get; set; }
        public decimal? Latitud { get; set; }
        public decimal? Longitud { get; set; }
    }

    /// <summary>Respuesta de Entregar e Incidencia.</summary>
    public class EstadoEnvioRepartoDTO
    {
        public int EnvioId { get; set; }
        public string Estado { get; set; }
        /// <summary>La página del enlace de pago (CobroReembolso = "Enlace"); null si no hay enlace.</summary>
        public string UrlPago { get; set; }
        /// <summary>NestoAPI#772: los bultos que han salido de la furgoneta con esta entrega. Null sin la tabla de la furgoneta.</summary>
        public int? BultosEntregados { get; set; }
    }

    /// <summary>
    /// GET api/RepartoPropio/MisCobros?fecha=yyyy-MM-dd: el extracto contable de ese día de la caja del usuario (su CajaDefecto),
    /// el mismo que el de la ventana de Cajas de Nesto (api/Informes/ExtractoContable).
    /// </summary>
    public class MisCobrosRepartoDTO
    {
        public string Cuenta { get; set; }
        public DateTime Fecha { get; set; }
        public decimal SaldoAnterior { get; set; }
        public System.Collections.Generic.List<ApunteCajaRepartoDTO> Apuntes { get; set; } = new System.Collections.Generic.List<ApunteCajaRepartoDTO>();
        public decimal TotalDia { get; set; }
        public decimal SaldoFinal { get; set; }
    }

    public class ApunteCajaRepartoDTO
    {
        public DateTime Fecha { get; set; }
        public string Concepto { get; set; }
        /// <summary>Debe − Haber de la cuenta de caja: positivo = entra en caja.</summary>
        public decimal Importe { get; set; }
        public int? Asiento { get; set; }
        public string Documento { get; set; }
    }

    /// <summary>GET api/RepartoPropio/Envios/{envioId}/PruebaEntrega (la firma va aparte, en .../Firma).</summary>
    public class PruebaEntregaDTO
    {
        public int EnvioId { get; set; }
        public DateTime FechaHora { get; set; }
        public string Receptor { get; set; }
        public string DniReceptor { get; set; }
        public string CobroReembolso { get; set; }
        public decimal? ImporteCobrado { get; set; }
        /// <summary>La caja en la que se contabilizó el efectivo (null si no se contabilizó).</summary>
        public string CuentaCaja { get; set; }
        /// <summary>El enlace de pago que se mandó (cobro por enlace), o null.</summary>
        public string UrlPago { get; set; }
        public string Usuario { get; set; }
        public bool TieneFirma { get; set; }
        public decimal? Latitud { get; set; }
        public decimal? Longitud { get; set; }
    }

    /// <summary>Una fila de EnviosAgenciaPruebasEntrega (entrega o incidencia), sin la firma.</summary>
    public class PruebaEntregaReparto
    {
        public const string TIPO_ENTREGA = "Entrega";
        public const string TIPO_INCIDENCIA = "Incidencia";
        public const string COBRO_EFECTIVO = "Efectivo";
        /// <summary>Ya no se admite (el repartidor no lleva datáfono, Carlos 29/10/26): da 400.</summary>
        public const string COBRO_TARJETA = "Tarjeta";
        public const string COBRO_ENLACE = "Enlace";
        public const string COBRO_NO_COBRADO = "NoCobrado";

        public int Id { get; set; }
        public int EnvioId { get; set; }
        public Guid ClaveIdempotencia { get; set; }
        public string Tipo { get; set; }
        public DateTime FechaHora { get; set; }
        public string Receptor { get; set; }
        public string DniReceptor { get; set; }
        public string CobroReembolso { get; set; }
        public decimal? ImporteCobrado { get; set; }
        public string Motivo { get; set; }
        public string Texto { get; set; }
        public decimal? Latitud { get; set; }
        public decimal? Longitud { get; set; }
        public int? Asiento { get; set; }
        /// <summary>
        /// NestoAPI#739: la cuenta de caja contra la que se contabilizó el reembolso cobrado: la CajaDefecto del usuario que
        /// entrega (cada repartidor en su caja). Null si no se contabilizó nada.
        /// </summary>
        public string CuentaCaja { get; set; }
        /// <summary>Cobro por enlace: el PagoTPV del enlace y su página (para devolver lo mismo si Ariadna reintenta).</summary>
        public int? PagoTPVId { get; set; }
        public string UrlPago { get; set; }
        public string Usuario { get; set; }
        public DateTime FechaRegistro { get; set; }
        public bool TieneFirma { get; set; }
        /// <summary>NestoAPI#759: los bultos recogidos (notas de recogida); null en lo demás o sin la columna del script.</summary>
        public short? BultosRecogidos { get; set; }
    }
}
