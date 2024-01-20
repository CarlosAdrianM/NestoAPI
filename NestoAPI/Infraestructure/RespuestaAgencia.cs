namespace NestoAPI.Infraestructure
{
    public class RespuestaAgencia
    {
        public string DireccionFormateada { get; set; } // para servicio 2h no se usa
        public double Longitud { get; set; } // para servicio 2h no se usa
        public double Latitud { get; set; } // para servicio 2h no se usa
        public decimal Coste { get; set; }
        public string Almacen { get; set; }
        public bool CondicionesPagoValidas { get; set; }
    }
}