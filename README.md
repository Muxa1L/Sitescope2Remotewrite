# Sitescope2Remotewrite
Created to collect metrics from HP/HPE/MicroFocus SiteScope and store them to any target supporting prometheus'es remotewrite.

In SiteScope you need to create Data Integration, and set receiver Url to <host:port>/send
Then sitescope starts sending xml's to this url in such format
<details>
  <summary>XML</summary>
  
  ```<performanceMonitors collector="SiteScope" collectorHost="0330ss01">
  <group name="SomeMR" desc="">
    <group name="SomeSystem" desc="">
      <group name="3416someserver02" desc="">
        <group name="MSMQ - queue size" desc="">
          <group name="[a0aabbbcc\countersinformation]" desc="">
            <monitor type="MSMQ_Queue_MsgCount" target="3416someserver02" targetIP="3416someserver02" time="1594388051000" quality="1" sourceTemplateName="" name="MSMQ_Queue_MsgCount">
              <counter value="0" quality="1" name="a0aabbbcc\countersinformation" />
              <counter value="0" quality="1" name="Value" />
            </monitor>
          </group>
        </group>
      </group>
    </group>
  </group> 
  <group name="МР Какой-то" desc="">
    <group name="SomeSystem" desc="">
      <group name="3416someserver02" desc="">
        <group name="MSMQ - размер очереди" desc="">
          <monitor type="MSMQ_Queue_MsgCount" target="3416someserver02" targetIP="3416someserver02" time="1594388051000" quality="1" sourceTemplateName="" name="MSMQ_Queue_MsgCount">
            <counter value="0" quality="1" name="a0aabbbcc\countersinformation" />
            <counter value="0" quality="1" name="Value" />
          </monitor>
          <monitor type="JSON_CustomAttr" target="3416someserver02" targetIP="3416someserver02" time="1594388051000" quality="1" sourceTemplateName="" name="JSON_CustomAttr">
            <counter value="0" quality="1" name="a0aabbbcc\countersinformation" />
            <counter value="aaa=213,bbb=332" quality="1" name="metrics" />
          </monitor>
          <monitor type="OS_WinDiskSpace" target="3416someserver02" targetIP="3416someserver02" time="1594388051000" quality="1" sourceTemplateName="" name="OS_WinDiskSpace">
            <counter value="0" quality="1" name="aaaa/aadsfadf1/addafdfsd2" />
            <counter value="aaa=213,bbb=332" quality="1" name="Value" />
          </monitor>
        </group>
      </group>
    </group>
  </group>
</performanceMonitors>
  ```
  
</details>

# Installation 
```
docker pull muxa1l/sitescope2remotewrite
```
TBC

# Configuration
Then we should configure this receiver. Via appsettings.json, or environment variables.

RemoteWrite.url - URL of remotewrite endpoint (VictoriaMetrics, Thanos, etc)
https://prometheus.io/docs/operating/integrations/#remote-endpoints-and-storage
