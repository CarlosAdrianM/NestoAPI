using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Rapports
{
    /// <summary>
    /// NestoAPI#603 (corte 1): respuesta de GET api/Clientes/SugerenciasContacto. A quién llamar hoy (por prioridad y
    /// cadencia) y a qué ritmo va el vendedor.
    /// </summary>
    public class SugerenciasContactoDTO
    {
        public string Vendedor { get; set; }
        public DateTime Fecha { get; set; }
        public RitmoContactosDTO Ritmo { get; set; }
        public List<SugerenciaContactoDTO> Sugerencias { get; set; } = new List<SugerenciaContactoDTO>();
    }

    /// <summary>NestoAPI#603: contactos hechos y objetivo para cubrir la cartera este mes.</summary>
    public class RitmoContactosDTO
    {
        /// <summary>
        /// Sugerencia 782 (Carlos, 26/10/26): CLIENTES DISTINTOS (número + contacto) de la cartera del objetivo
        /// (<see cref="MotorSugerenciasContacto.CarteraObjetivo"/>) con los que el vendedor ha hablado hoy (sus rapports
        /// Estado 0 de tipo T/V/W). Varios rapports al mismo cliente cuentan uno y un cliente que no es de la cartera no
        /// cuenta. Antes eran rapports (JGP: 268 rapports en octubre con 172 clientes distintos).
        /// </summary>
        public int ContactosHoy { get; set; }
        /// <summary>Ídem, de lunes a hoy (el cliente con rapports el lunes y hoy cuenta uno).</summary>
        public int ContactosSemana { get; set; }
        /// <summary>Ídem, del día 1 a hoy (el cliente con rapports ayer y hoy cuenta uno aquí, y también uno en <see cref="ContactosHoy"/>).</summary>
        public int ContactosMes { get; set; }
        /// <summary>
        /// Sugerencia 782: clientes distintos de la cartera contactados este mes ANTES de hoy. No es ContactosMes − ContactosHoy:
        /// quien se repite hoy ya estaba. Para el objetivo de hoy «del principio del día» de las frases. No va en la respuesta.
        /// </summary>
        [JsonIgnore]
        public int ContactosMesAntesDeHoy { get; set; }
        /// <summary>
        /// Sugerencia 782: clientes distintos de la cartera contactados cada día ANTERIOR a hoy (62 días; los días sin
        /// contactos faltan), con el criterio de <see cref="ContactosHoy"/>. Para la racha y el mejor día de las frases. No va
        /// en la respuesta.
        /// </summary>
        [JsonIgnore]
        public Dictionary<DateTime, int> ContactosPorDia { get; set; } = new Dictionary<DateTime, int>();
        /// <summary>Σ por cliente de la cartera del objetivo de <see cref="MotorSugerenciasContacto.LlamadasMes"/> (de 1 a 4).</summary>
        public int ObjetivoMes { get; set; }
        /// <summary>max(0, ObjetivoMes − ContactosMes (clientes distintos)) / días laborables que quedan (hoy incluido), redondeado hacia arriba.</summary>
        public int ObjetivoHoy { get; set; }
        /// <summary>Lunes a viernes sin festivos, de hoy (incluido si es laborable) a fin de mes.</summary>
        public int DiasLaborablesRestantesMes { get; set; }
        public int PendientesMaxima { get; set; }
        public int PendientesAlta { get; set; }
        public int PendientesMedia { get; set; }
        public int PendientesBaja { get; set; }
        /// <summary>NestoAPI#677: compradores (compra en 12 meses) sin ningún contacto desde el día 1 del mes.</summary>
        public int CompradoresSinContactoMes { get; set; }
        /// <summary>NestoAPI#677: sitios de la lista de hoy reservados para ellos: ⌈CompradoresSinContactoMes / DiasLaborablesRestantesMes⌉
        /// (el último laborable, todos).</summary>
        public int CupoCoberturaHoy { get; set; }
        public string Frase { get; set; }
    }

    /// <summary>
    /// NestoAPI#603: un cliente sugerido. Los nombres de las propiedades coinciden, sin distinguir mayúsculas, con los de
    /// <c>ClienteProbabilidadVenta</c> que pinta hoy Nesto (cliente, contacto, nombre, direccion, poblacion, telefono,
    /// Probabilidad, DiasDesdeUltimoPedido, DiasDesdeUltimaInteraccion): la clase de Nesto deserializa esta respuesta tal cual.
    /// </summary>
    public class SugerenciaContactoDTO
    {
        /// <summary>Id de la fila en SugerenciasContacto (registro del día).</summary>
        public int SugerenciaId { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public string Direccion { get; set; }
        public string CodigoPostal { get; set; }
        public string Telefono { get; set; }
        public string Poblacion { get; set; }
        public string Provincia { get; set; }
        /// <summary>"Máxima", "Alta", "Media" o "Baja".</summary>
        public string Prioridad { get; set; }
        /// <summary>1, 2, 3… en el orden en que conviene llamar.</summary>
        public int Orden { get; set; }
        public string Motivo { get; set; }
        /// <summary>Probabilidad del modelo actual (0 a 1). Desde NestoAPI#677 ordena la lista (y decide la prioridad Máxima).</summary>
        public float Probabilidad { get; set; }
        /// <summary>Días desde el último rapport en el que se habló con el cliente (Estado 0). Null si no consta ninguno.</summary>
        public int? DiasDesdeUltimoContacto { get; set; }
        /// <summary>Días desde el último pedido (facturado o todavía en curso).</summary>
        public int DiasDesdeUltimoPedido { get; set; }
        /// <summary>Compatibilidad con ClienteProbabilidadVenta: DiasDesdeUltimoContacto, o 9999 si no consta ninguno.</summary>
        public int DiasDesdeUltimaInteraccion => DiasDesdeUltimoContacto ?? 9999;
        public int CadenciaDias { get; set; }
        public int PedidosUltimos12Meses { get; set; }
        public decimal ImporteUltimos12Meses { get; set; }
        public string GrupoSubgrupoMasVendido { get; set; }
        /// <summary>True si ya hay un rapport de este cliente hoy.</summary>
        public bool Atendida { get; set; }
        /// <summary>NestoAPI#593: en los últimos días de una campaña, el cliente tiene el cheque regalo sin usar (el motivo
        /// empieza por «Tiene un cheque de 50 € sin usar hasta el 07/11»). Ver <see cref="ImpulsoChequesRegaloContacto"/>.</summary>
        public bool TieneChequeRegalo { get; set; }
        /// <summary>NestoAPI#677: por qué entró en la lista: "Probabilidad", "Cobertura" (cupo de compradores sin contacto este
        /// mes) o "ChequeRegalo". Ver <see cref="OrigenesSugerenciaContacto"/>. Campo nuevo: los clientes que no lo lean no cambian.</summary>
        public string Origen { get; set; }
    }

    /// <summary>NestoAPI#603: GET api/Clientes/SugerenciasContacto/Uso, una fila por vendedor.</summary>
    public class UsoSugerenciasContactoDTO
    {
        public string Vendedor { get; set; }
        public int DiasConSugerencias { get; set; }
        public int Sugeridas { get; set; }
        public int Atendidas { get; set; }
        /// <summary>Atendidas / Sugeridas (0 a 1).</summary>
        public double PorcentajeAtendidas { get; set; }
        /// <summary>Rapports del vendedor en el periodo con Estado 0 (se habló) o 1 (no contestó), de cualquier tipo.</summary>
        public int RapportsTotales { get; set; }
        /// <summary>NestoAPI#677: sugeridas y atendidas por origen (Probabilidad, Cobertura, ChequeRegalo). Antes del corte 1
        /// todo sale como Probabilidad: para comparar, pedir desde el día del despliegue.</summary>
        public List<UsoOrigenSugerenciasContactoDTO> PorOrigen { get; set; } = new List<UsoOrigenSugerenciasContactoDTO>();
    }

    /// <summary>NestoAPI#677: una fila de <see cref="UsoSugerenciasContactoDTO.PorOrigen"/>.</summary>
    public class UsoOrigenSugerenciasContactoDTO
    {
        public string Origen { get; set; }
        public int Sugeridas { get; set; }
        public int Atendidas { get; set; }
        /// <summary>Atendidas / Sugeridas (0 a 1).</summary>
        public double PorcentajeAtendidas { get; set; }
    }

    /// <summary>NestoAPI#603: un cliente de la cartera con lo que el motor necesita para decidir.</summary>
    public class ClienteCarteraContacto
    {
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public string Direccion { get; set; }
        public string CodigoPostal { get; set; }
        public string Telefono { get; set; }
        public string Poblacion { get; set; }
        public string Provincia { get; set; }
        /// <summary>Días distintos con compra (LinPedidoVta Estado 4, base &gt; 0, subgrupo ≠ MMP) en 12 meses. NestoAPI#620: si el
        /// vendedor lleva al cliente solo por grupo de producto (peluquería), solo las líneas de ese grupo (también en el resto de compras y el último pedido).</summary>
        public int Pedidos12Meses { get; set; }
        public decimal Importe12Meses { get; set; }
        /// <summary>Ídem en 24 meses (≥ 1 para estar en la cartera).</summary>
        public int Pedidos24Meses { get; set; }
        /// <summary>Último pedido: el más reciente entre lo facturado y lo que está en curso (pendiente, en curso o albarán).</summary>
        public DateTime? UltimoPedido { get; set; }
        /// <summary>
        /// NestoAPI#620: el último pedido del cliente de CUALQUIER grupo (60 días, facturado o no). Solo para «pidió hace
        /// poco» (<see cref="UmbralesSugerenciasContacto.DIAS_TRAS_PEDIDO"/>): vale para todos sus vendedores.
        /// </summary>
        public DateTime? UltimoPedidoCliente { get; set; }
        /// <summary>Último rapport Estado 0 (se habló) de tipo T/V/W. NestoAPI#620: sin los del otro vendedor que lleva al cliente.</summary>
        public DateTime? UltimoContacto { get; set; }
        /// <summary>Último rapport Estado 1 (no contestó) de tipo T/V/W.</summary>
        public DateTime? UltimoIntento { get; set; }
        public float Probabilidad { get; set; }
        public string GrupoSubgrupoMasVendido { get; set; }

        public string Clave => ClaveDe(Cliente, Contacto);

        public static string ClaveDe(string cliente, string contacto) => $"{cliente?.Trim()}/{contacto?.Trim()}";
    }

    /// <summary>NestoAPI#603: lo que devuelve el modelo para un cliente.</summary>
    public class PrediccionContacto
    {
        public float Probabilidad { get; set; }
        public string GrupoSubgrupoMasVendido { get; set; }
    }

    /// <summary>
    /// NestoAPI#603 / sugerencia 782: clientes distintos de la cartera del objetivo con los que ha hablado el vendedor hoy,
    /// esta semana, este mes, este mes antes de hoy y cada día anterior (ver <see cref="RitmoContactosDTO.ContactosHoy"/>).
    /// Lo calcula <see cref="MotorSugerenciasContacto.ContarContactos"/>.
    /// </summary>
    public class ContactosVendedor
    {
        public int Hoy { get; set; }
        public int Semana { get; set; }
        public int Mes { get; set; }
        public int MesAntesDeHoy { get; set; }
        /// <summary>Días anteriores a hoy (los días sin contactos faltan).</summary>
        public Dictionary<DateTime, int> PorDia { get; set; } = new Dictionary<DateTime, int>();
    }

    /// <summary>Sugerencia 782: un cliente (número + contacto) con el que el vendedor habló un día (rapport Estado 0 T/V/W).</summary>
    public class ContactoClienteDia
    {
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public DateTime Dia { get; set; }

        public string Clave => ClienteCarteraContacto.ClaveDe(Cliente, Contacto);
    }
}
