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
        /// <summary>Rapports Estado 0 (se habló) de tipo T/V/W del vendedor hoy.</summary>
        public int ContactosHoy { get; set; }
        /// <summary>Ídem, de lunes a hoy.</summary>
        public int ContactosSemana { get; set; }
        /// <summary>Ídem, del día 1 a hoy.</summary>
        public int ContactosMes { get; set; }
        /// <summary>Σ por cliente de la cartera de max(1, redondeo(días laborables del mes / cadencia)).</summary>
        public int ObjetivoMes { get; set; }
        /// <summary>max(0, ObjetivoMes − ContactosMes) / días laborables que quedan (hoy incluido), redondeado hacia arriba.</summary>
        public int ObjetivoHoy { get; set; }
        /// <summary>Lunes a viernes sin festivos, de hoy (incluido si es laborable) a fin de mes.</summary>
        public int DiasLaborablesRestantesMes { get; set; }
        public int PendientesMaxima { get; set; }
        public int PendientesAlta { get; set; }
        public int PendientesMedia { get; set; }
        public int PendientesBaja { get; set; }
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
        /// <summary>Probabilidad del modelo actual (0 a 1). Solo desempata y decide la prioridad Máxima.</summary>
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
        /// <summary>Días distintos con compra (LinPedidoVta Estado 4, base &gt; 0, subgrupo ≠ MMP) en 12 meses.</summary>
        public int Pedidos12Meses { get; set; }
        public decimal Importe12Meses { get; set; }
        /// <summary>Ídem en 24 meses (≥ 1 para estar en la cartera).</summary>
        public int Pedidos24Meses { get; set; }
        /// <summary>Último pedido: el más reciente entre lo facturado y lo que está en curso (pendiente, en curso o albarán).</summary>
        public DateTime? UltimoPedido { get; set; }
        /// <summary>Último rapport Estado 0 (se habló) de tipo T/V/W.</summary>
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

    /// <summary>NestoAPI#603: rapports Estado 0 del vendedor hoy, esta semana y este mes.</summary>
    public class ContactosVendedor
    {
        public int Hoy { get; set; }
        public int Semana { get; set; }
        public int Mes { get; set; }
    }
}
