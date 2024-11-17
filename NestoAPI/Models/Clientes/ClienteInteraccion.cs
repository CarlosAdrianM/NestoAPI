namespace NestoAPI.Models.Clientes
{
    public class ClienteInteraccion
    {
        public string ClienteId { get; set; }
        public string TipoInteraccion { get; set; }
        public string MesActual { get; set; }
        public string DiaDeLaSemana { get; set; }
        public string GrupoSubgrupoMasVendido { get; set; }
        public float EsPorLaTarde { get; set; }
        public float DiasDesdeUltimaInteraccion { get; set; }
        public float DiasDesdeUltimoPedido { get; set; }
        public float FrecuenciaPedidosUltimoAnno { get; set; }
        public float InteraccionesUltimos12Meses { get; set; }
        public float PedidosMesAnnoAnterior { get; set; }
        public bool Target { get; set; }

    }
}
