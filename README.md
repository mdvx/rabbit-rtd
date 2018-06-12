# rabbit-rtd
Excel RTD server sourcing data from Rabbit-MQ


## Installation
1. Clone the repository and go to its folder.
2. Compile the code using Visual Studio, MSBuild or via this handy script file:

   `build.cmd`


3. Register the COM server by running the following script in admin command prompt:
   
   `register.cmd`

## Usage

Once the RTD server has been installed, you can use it from Excel via the RTD macro.
This is the syntax:

`=RTD("rabbit",, $HOST_URI, $EXCHANGE, $ROUTING_KEY)`   // returns whole message as a string

`=RTD("rabbit",, $HOST_URI, $EXCHANGE, [$QUEUE,] $ROUTING_KEY, $FIELD)`  // requires JSON formatted messages

`=RTD("rabbit",, "localhost", "my.x", "my.q", "my.rk)`   // returns whole message as a string

### $HOST_URI
Format
   [userName:password@]hostName[:portNumber][/virtualHost]

Examples:
   localhost
   guest:guest@localhost:5672/
   
### $EXCHANGE
Format:
   [type:][name][[[:durability]:auto-delete]:arguments]
   
type:
* amq.direct     (default)
* amq.fanout
* amq.topic
* amq.match
   
name:
* ""  - empty string
* my.exchange.name
   
durability:  exchanges survive broker restart
* [true|false]
   
auto-delete:  exchange is deleted when last queue is unbound from it
* [true|false]
   
arguments:  optional, used by plugins and broker-specific features
   
   
   
Examples:
   my.exchange::
   amq.topic:my.exchange
   EXCHANGE                      

### $QUEUE

### $ROUTING_KEY

### $FIELD


EXCHANGE should be declared as type: "topic", autoDelete: true

![Excel screenshot](doc/ice_video.gif)

