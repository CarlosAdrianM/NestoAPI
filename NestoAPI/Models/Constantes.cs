using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models
{
    public class Constantes
    {
        public class Empresas
        {
            public const string DELEGACION_POR_DEFECTO = "ALG";
            public const string EMPRESA_ESPEJO_POR_DEFECTO = "3";
            public const string EMPRESA_POR_DEFECTO = "1";
            public const string FORMA_VENTA_POR_DEFECTO = "VAR";
        }

        public class EstadosLineaVenta
        {
            public const int PENDIENTE = -1;
            public const int EN_CURSO = 1;
            public const int ALBARAN = 2;
            public const int FACTURA = 4;
        }

        public class Productos
        {
            public const string ALMACEN_POR_DEFECTO = "ALG";
            public const string ALMACEN_TIENDA = "REI";
            public const short ESTADO_NO_SOBRE_PEDIDO = 0;
        }

        public class TiposLineaVenta
        {
            public const int TEXTO = 0;
            public const int PRODUCTO = 1;
            public const int CUENTA_CONTABLE = 2;
            public const int INMOVILIZADO = 3;
        }

        public class Ubicaciones
        {
            public const int UBICADO = 0;
            public const int PENDIENTE_UBICAR = 2;
            public const int RESERVADO_PICKING = 3;
            public const int RESERVADO_REPOSICION = 3;
        }

        public class Picking
        {
            public const int HORA_MAXIMA_AMPLIAR_PEDIDOS = 11;
        }

        public class ClientesEspeciales
        {
            public const string EL_EDEN = "15191";
        }

    }
}