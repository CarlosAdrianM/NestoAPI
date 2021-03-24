using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.Depositos
{
    public class DatosCorreoDeposito
    {       
        public string Nombre { get; set; }
        public string Enlace { get; set; }
        public DateTime FechaPrimerMovimiento { get; set; }
        public string Imagen { get; set; }
        public string ProductoId { get; set; }
        public int UnidadesDevueltas { get; set; }
        public int UnidadesEnviadasProveedor { get; set; }
        public int UnidadesReservadas { get; set; }
        public int UnidadesStock { get; set; }
        public int UnidadesVendidas { get; set; } // para el cálculo de cuando reponer

       


        public string ColorTexto { 
            get {
                if ((DiasStock > 90 && DiasAntiguedad > 15) || UnidadesEnviadasProveedor < 0)
                {
                    return "#FF0000"; //rojo -> retirar
                }
                else if (DiasStock < 7)
                {
                    return "#008000"; // verde -> reponer
                }
                else
                {
                    return "#000000"; //negro -> Esperar
                }
            } 
        }
        public int DiasAntiguedad { 
            get
            {
                int dias = (int)(DateTime.Today - FechaPrimerMovimiento).TotalDays;
                if (dias == 0)
                {
                    dias = 1;
                }
                return dias > Constantes.Productos.DEPOSITO_DIAS_ESTADISTICA ? Constantes.Productos.DEPOSITO_DIAS_ESTADISTICA : dias;
            } 
        }
        public int DiasStock
        {
            get
            {
                if (VentaDiaria == 0)
                {
                    return 0;
                }
                return (int)Decimal.Divide(UnidadesStock, VentaDiaria);
            }
        }
        public int TotalUnidadesVendidas
        {
            get
            {
                return UnidadesVendidas - UnidadesDevueltas + UnidadesReservadas;
            }
        }
        public int UnidadesPendientesFacturar { 
            get {
                int cantidad = UnidadesEnviadasProveedor - UnidadesStock + UnidadesReservadas;
                return cantidad < 0 ? 0 : cantidad;
            } 
        }
        public decimal VentaDiaria
        {
            get
            {
                if (DiasAntiguedad == 0)
                {
                    return 0;
                }
                return Decimal.Divide(TotalUnidadesVendidas, DiasAntiguedad);
            }
        }
    }
}