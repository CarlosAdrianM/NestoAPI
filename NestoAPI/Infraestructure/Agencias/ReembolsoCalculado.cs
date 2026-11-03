namespace NestoAPI.Infraestructure.Agencias
{
    /// <summary>
    /// NestoAPI#718: el reembolso de la etiqueta con lo necesario para avisar si se ha corregido. Un pedido que se factura por
    /// la empresa espejo (Global, serie GB, todo al 0 %) cobra la base de sus líneas, aunque en la empresa 1 alguna esté al 21 %
    /// (928370: 182,44 según las líneas, 175,93 la factura; 929262: 440,18 frente a 450,05).
    /// </summary>
    public class ReembolsoCalculado
    {
        /// <summary>Lo que hay que cobrar: lo que cobrará la factura.</summary>
        public decimal Importe { get; set; }

        /// <summary>La suma de los Total de las líneas tal como están (lo que salía antes de NestoAPI#718).</summary>
        public decimal ImporteSegunLineas { get; set; }

        public bool FacturaPorEmpresaEspejo { get; set; }

        /// <summary>Entre las líneas que se cobran, unas llevan IVA y otras no.</summary>
        public bool IvaMezclado { get; set; }

        public int Pedido { get; set; }

        /// <summary>El importe se ha corregido: la suma de las líneas no es lo que cobrará la factura.</summary>
        public bool Corregido => FacturaPorEmpresaEspejo && Importe != ImporteSegunLineas;

        /// <summary>Para ELMAH y la historia del envío; null si no se ha corregido nada.</summary>
        public string Explicacion => !Corregido ? null :
            $"NestoAPI#718: el pedido {Pedido} se factura por Global (empresa espejo, sin IVA) y " +
            (IvaMezclado ? "sus líneas tienen porcentajes de IVA mezclados" : "sus líneas llevan IVA") +
            $": el reembolso es lo que cobrará la factura ({Importe:N2} €) y no la suma de las líneas ({ImporteSegunLineas:N2} €).";

        internal static ReembolsoCalculado Sin(decimal importe) => new ReembolsoCalculado { Importe = importe, ImporteSegunLineas = importe };
    }
}
